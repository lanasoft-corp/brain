using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LanaSoft.Brain.Mcp.Mem0;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace LanaSoft.Brain.Mcp.Tests;

/// <summary>
/// Passerelle hébergée en mémoire : jetons signés par une clé de test au lieu du JWKS Keycloak,
/// Mem0 remplacé par <see cref="FakeMem0"/>.
/// </summary>
public sealed class BrainMcpFactory : WebApplicationFactory<Program>
{
    public const string Issuer = "https://auth.example.test/realms/demo";
    public const string Audience = "https://brain.example.test/mcp";
    public const string Owner = "alice";

    private static readonly SymmetricSecurityKey SigningKey = new(Encoding.UTF8.GetBytes(new string('k', 64)));

    public FakeMem0 Mem0 { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Brain:Resource", Audience);
        builder.UseSetting("Brain:Authority", Issuer);
        builder.UseSetting("Brain:AgentOwner", Owner);
        builder.UseSetting("Brain:Mem0Url", "http://mem0.test");
        builder.UseSetting("Brain:Mem0ApiKey", "m0sk_test");
        builder.ConfigureServices(services =>
        {
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
            {
                // Pas d'appel au fournisseur d'identité : configuration et clé fournies directement.
                o.Configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
                o.TokenValidationParameters.IssuerSigningKey = SigningKey;
            });
            services.AddHttpClient<Mem0Client>().ConfigurePrimaryHttpMessageHandler(() => Mem0);
        });
    }

    /// <summary>Jeton de test. Par défaut : alice via claude-code, scopes brain et brain:write.</summary>
    public static string Token(
        string scope = "brain brain:write",
        string audience = Audience,
        string username = Owner,
        string azp = "claude-code",
        string issuer = Issuer)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
            Subject = new ClaimsIdentity(
            [
                new Claim("sub", $"sub-{username}"),
                new Claim("preferred_username", username),
                new Claim("azp", azp),
                new Claim("scope", scope),
            ]),
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    public HttpClient ClientWith(string? token)
    {
        var client = CreateClient();
        if (token is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}

/// <summary>Mem0 en mémoire : juste ce que la passerelle appelle.</summary>
public sealed class FakeMem0 : HttpMessageHandler
{
    private readonly List<JsonObject> _memories = [];

    public IReadOnlyList<JsonObject> Memories => _memories;
    public List<JsonObject> AddRequests { get; } = [];

    /// <summary>Nombre de PUT réussis avant une panne simulée ; négatif : jamais de panne.</summary>
    public int FailUpdatesAfter { get; set; } = -1;

    /// <summary>Recherche en panne simulée.</summary>
    public bool FailSearches { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var path = request.RequestUri!.AbsolutePath.Trim('/');
        var body = request.Content is null ? null : JsonNode.Parse(await request.Content.ReadAsStringAsync(ct))?.AsObject();

        switch (request.Method.Method, path)
        {
            case ("POST", "memories"):
                AddRequests.Add(body!);
                var memory = new JsonObject
                {
                    ["id"] = Guid.NewGuid().ToString(),
                    ["memory"] = body!["messages"]![0]!["content"]!.GetValue<string>(),
                    ["user_id"] = body["user_id"]!.GetValue<string>(),
                    ["agent_id"] = body["agent_id"]?.GetValue<string>(),
                    ["metadata"] = body["metadata"]!.DeepClone(),
                    ["created_at"] = DateTimeOffset.UtcNow.ToString("O"),
                };
                _memories.Add(memory);
                return Json(new JsonObject { ["results"] = new JsonArray(With(memory, "event", "ADD")) });

            case ("POST", "search"):
                if (FailSearches) return new HttpResponseMessage(HttpStatusCode.BadGateway);
                var user = body!["filters"]!["user_id"]!.GetValue<string>();
                var project = body["filters"]!["project"]?.GetValue<string>();
                var words = body["query"]!.GetValue<string>().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var hits = _memories
                    .Where(m => m["user_id"]!.GetValue<string>() == user)
                    .Where(m => project is null || m["metadata"]!["project"]?.GetValue<string>() == project)
                    .Select(m => (m, score: words.Count(w => m["memory"]!.GetValue<string>().ToLowerInvariant().Contains(w))))
                    .Where(x => x.score > 0)
                    .OrderByDescending(x => x.score)
                    .Select(x => (JsonNode)Scored(x.m, x.score / (double)words.Length, body["explain"]?.GetValue<bool>() == true));
                return Json(new JsonObject { ["results"] = new JsonArray(hits.ToArray()) });

            case ("GET", "memories"):
                var owner = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query)["user_id"];
                return Json(new JsonObject
                {
                    ["results"] = new JsonArray(_memories.Where(m => m["user_id"]!.GetValue<string>() == owner).Select(m => (JsonNode)m.DeepClone()).ToArray()),
                });

            case ("GET", var p) when p.StartsWith("memories/"):
                var found = _memories.FirstOrDefault(m => m["id"]!.GetValue<string>() == p["memories/".Length..]);
                return found is null ? new HttpResponseMessage(HttpStatusCode.NotFound) : Json(found.DeepClone());

            case ("PUT", var p) when p.StartsWith("memories/"):
                var target = _memories.FirstOrDefault(m => m["id"]!.GetValue<string>() == p["memories/".Length..]);
                if (target is null) return new HttpResponseMessage(HttpStatusCode.NotFound);
                if (FailUpdatesAfter-- == 0) return new HttpResponseMessage(HttpStatusCode.BadGateway);
                foreach (var (key, value) in body!["metadata"]!.AsObject())
                    target["metadata"]![key] = value?.DeepClone();
                return Json(new JsonObject { ["message"] = "Memory updated successfully!" });

            case ("DELETE", var p) when p.StartsWith("memories/"):
                _memories.RemoveAll(m => m["id"]!.GetValue<string>() == p["memories/".Length..]);
                return Json(new JsonObject { ["message"] = "Memory deleted successfully" });
        }
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    // Comme Mem0 : le score hybride est divisé par un maximum (ici 2) ; explain donne le score sémantique.
    private static JsonObject Scored(JsonObject source, double semantic, bool explain)
    {
        var copy = With(source, "score", semantic / 2);
        if (explain) copy["score_details"] = new JsonObject { ["semantic_score"] = semantic };
        return copy;
    }

    private static JsonObject With(JsonObject source, string key, JsonNode? value)
    {
        var copy = source.DeepClone().AsObject();
        copy[key] = value;
        return copy;
    }

    private static HttpResponseMessage Json(JsonNode node) =>
        new(HttpStatusCode.OK) { Content = new StringContent(node.ToJsonString(), Encoding.UTF8, "application/json") };
}
