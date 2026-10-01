using System.Security.Claims;

namespace LanaSoft.Brain.Mcp.Identity;

/// <summary>
/// Identité de l'appelant, tirée uniquement du jeton validé : jamais d'un paramètre d'outil.
/// </summary>
/// <param name="UserId">Propriétaire des mémoires (user_id Mem0).</param>
/// <param name="AgentId">
/// Client qui a obtenu le jeton (azp, ou son nom court s'il figure dans les alias) : claude-code, agent-trading...
/// </param>
public sealed record Caller(string UserId, string AgentId)
{
    // Préfixe du nom d'utilisateur que Keycloak donne au compte de service d'un client
    // (grant client credentials).
    private const string ServiceAccountPrefix = "service-account-";

    public static Caller From(ClaimsPrincipal principal, BrainOptions options)
    {
        var azp = principal.FindFirstValue("azp")
            ?? throw new InvalidOperationException("Jeton sans azp.");
        var agentId = options.AgentAliases.FirstOrDefault(a => a.ClientId == azp)?.Name ?? azp;
        var username = principal.FindFirstValue("preferred_username");

        var userId = string.IsNullOrEmpty(username) || username.StartsWith(ServiceAccountPrefix, StringComparison.Ordinal)
            ? options.AgentOwner
            : username;

        return new Caller(userId, agentId);
    }

    /// <summary>Clé de limitation de débit : un compteur par identité.</summary>
    public static string RateLimitKey(ClaimsPrincipal principal) =>
        principal.FindFirstValue("sub") ?? principal.FindFirstValue("azp") ?? "anonymous";
}
