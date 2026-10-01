using System.Threading.RateLimiting;
using LanaSoft.Brain.Mcp;
using LanaSoft.Brain.Mcp.Identity;
using LanaSoft.Brain.Mcp.Mem0;
using LanaSoft.Brain.Mcp.Tools;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Authentication;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<BrainOptions>(builder.Configuration.GetSection(BrainOptions.Section));
var brain = builder.Configuration.GetSection(BrainOptions.Section).Get<BrainOptions>() ?? new BrainOptions();
brain.Validate();

// Corps de requête plafonné : un fait ou un résumé ne dépasse pas quelques Ko.
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 64 * 1024);

// TLS terminé par le reverse proxy : schéma et hôte d'origine pour construire l'URL des métadonnées.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

// Jetons du fournisseur d'identité (Keycloak) : signature (JWKS du realm), émetteur, expiration et
// audience obligatoires. L'audience vient des scopes brain et brain:write (mapper d'audience côté
// Keycloak, qui ne gère pas encore le paramètre resource de RFC 8707).
builder.Services
    .AddAuthentication(o =>
    {
        o.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        o.DefaultChallengeScheme = McpAuthenticationDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(o =>
    {
        o.Authority = brain.Authority;
        if (!string.IsNullOrWhiteSpace(brain.MetadataAddress)) o.MetadataAddress = brain.MetadataAddress;
        o.RequireHttpsMetadata = brain.RequireHttpsMetadata;
        o.MapInboundClaims = false;
        o.TokenValidationParameters.ValidIssuer = brain.Authority;
        o.TokenValidationParameters.ValidAudience = brain.Resource;
        o.TokenValidationParameters.NameClaimType = "preferred_username";
    })
    // Réponse 401 avec WWW-Authenticate (resource_metadata) et document RFC 9728 publié sur
    // /.well-known/oauth-protected-resource.
    .AddMcp(o =>
    {
        o.ResourceMetadata = new ProtectedResourceMetadata
        {
            Resource = brain.Resource,
            AuthorizationServers = [brain.Authority],
            ScopesSupported = [BrainTools.ReadPolicy, BrainTools.WritePolicy],
            ResourceName = "brain (mémoire partagée)",
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(BrainTools.ReadPolicy, p => p.RequireAuthenticatedUser().RequireAssertion(c => HasScope(c.User, BrainTools.ReadPolicy)))
    .AddPolicy(BrainTools.WritePolicy, p => p.RequireAuthenticatedUser().RequireAssertion(c => HasScope(c.User, BrainTools.WritePolicy)));

// Débit limité par identité (sub du jeton) : un client qui boucle ne sature ni Mem0 ni les API
// d'extraction et d'embedding.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("per-caller", ctx => RateLimitPartition.GetFixedWindowLimiter(
        Caller.RateLimitKey(ctx.User),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = brain.RequestsPerMinute, Window = TimeSpan.FromMinutes(1) }));
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient<Mem0Client>(c =>
{
    c.BaseAddress = new Uri(new Uri(brain.Mem0Url), "./");
    c.DefaultRequestHeaders.Add("X-API-Key", brain.Mem0ApiKey);
    // Une extraction par Haiku (session_summary) peut prendre plusieurs secondes.
    c.Timeout = TimeSpan.FromSeconds(90);
});

builder.Services.AddMcpServer(o => o.ServerInfo = new() { Name = "brain", Version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "0" })
    .WithHttpTransport(o => o.Stateless = true)
    .AddAuthorizationFilters()
    .WithTools<BrainTools>();

builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseForwardedHeaders();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapHealthChecks("/healthz");
app.MapMcp("/mcp").RequireAuthorization(BrainTools.ReadPolicy).RequireRateLimiting("per-caller");

await app.RunAsync();

static bool HasScope(System.Security.Claims.ClaimsPrincipal user, string scope) =>
    user.FindAll("scope").SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Contains(scope);

// Point d'entrée exposé aux tests (WebApplicationFactory<Program>).
public partial class Program
{
    protected Program() { }
}
