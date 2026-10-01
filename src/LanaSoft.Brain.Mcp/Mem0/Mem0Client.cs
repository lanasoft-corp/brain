using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LanaSoft.Brain.Mcp.Mem0;

/// <summary>Une mémoire telle que renvoyée aux clients MCP.</summary>
public sealed record Memory(
    string Id,
    string Text,
    string? Project,
    string? Kind,
    string? AgentId,
    DateTimeOffset? CreatedAt,
    double? Score);

/// <summary>Client de l'API REST Mem0 (dans le cluster, clé m0sk_ en X-API-Key).</summary>
public sealed class Mem0Client(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Recherche hybride de Mem0. Son score combine sémantique, BM25 et entités, divisé par un
    /// maximum qui dépend de la requête : il classe, mais ne se compare pas d'une requête à l'autre.
    /// <paramref name="semanticScore"/> remplace ce score par la similarité cosinus seule, qui se
    /// compare à un seuil fixe.
    /// </summary>
    public async Task<IReadOnlyList<Memory>> SearchAsync(
        string userId, string query, string? project, int limit, CancellationToken ct, bool semanticScore = false)
    {
        var filters = new Dictionary<string, string> { ["user_id"] = userId };
        if (!string.IsNullOrWhiteSpace(project)) filters["project"] = project;

        var response = await http.PostAsJsonAsync(
            "search", new { query, filters, top_k = limit, explain = semanticScore ? true : (bool?)null }, Json, ct);
        return ReadMemories(await ReadJsonAsync(response, ct), semanticScore);
    }

    public async Task<IReadOnlyList<Memory>> ListAsync(string userId, CancellationToken ct)
    {
        var response = await http.GetAsync($"memories?user_id={Uri.EscapeDataString(userId)}&top_k=1000", ct);
        return ReadMemories(await ReadJsonAsync(response, ct));
    }

    public async Task<IReadOnlyList<Memory>> AddAsync(
        string userId, string agentId, string text, bool infer,
        IReadOnlyDictionary<string, string> metadata, string? runId, CancellationToken ct)
    {
        var body = new
        {
            messages = new[] { new { role = "user", content = text } },
            user_id = userId,
            agent_id = agentId,
            run_id = runId,
            infer,
            metadata,
        };
        var response = await http.PostAsJsonAsync("memories", body, Json, ct);
        return ReadMemories(await ReadJsonAsync(response, ct));
    }

    /// <summary>Renvoie le propriétaire (user_id) d'une mémoire, ou null si elle n'existe pas.</summary>
    public async Task<string?> GetOwnerAsync(string memoryId, CancellationToken ct)
    {
        var response = await http.GetAsync($"memories/{Uri.EscapeDataString(memoryId)}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        var json = await ReadJsonAsync(response, ct);
        return json.ValueKind == JsonValueKind.Object && json.TryGetProperty("user_id", out var u)
            ? u.GetString()
            : null;
    }

    /// <summary>
    /// Fusionne des métadonnées dans une mémoire. Mem0 garde le texte, l'identifiant, created_at
    /// et agent_id ; il recalcule le vecteur du même texte et trace un UPDATE dans l'historique.
    /// </summary>
    public async Task UpdateMetadataAsync(
        string memoryId, IReadOnlyDictionary<string, string> metadata, CancellationToken ct)
    {
        var response = await http.PutAsJsonAsync(
            $"memories/{Uri.EscapeDataString(memoryId)}", new { metadata }, Json, ct);
        await ReadJsonAsync(response, ct);
    }

    public async Task DeleteAsync(string memoryId, CancellationToken ct)
    {
        var response = await http.DeleteAsync($"memories/{Uri.EscapeDataString(memoryId)}", ct);
        await ReadJsonAsync(response, ct);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
            throw new Mem0Exception((int)response.StatusCode);
        var text = await response.Content.ReadAsStringAsync(ct);
        return string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
    }

    // Mem0 renvoie soit { "results": [...] }, soit directement une liste.
    private static IReadOnlyList<Memory> ReadMemories(JsonElement json, bool semanticScore = false)
    {
        var items = json.ValueKind switch
        {
            JsonValueKind.Object when json.TryGetProperty("results", out var r) => r,
            JsonValueKind.Array => json,
            _ => default,
        };
        if (items.ValueKind != JsonValueKind.Array) return [];

        return items.EnumerateArray()
            .Where(e => e.TryGetProperty("id", out _) && e.TryGetProperty("memory", out _))
            .Select(e =>
            {
                var metadata = e.TryGetProperty("metadata", out var m) && m.ValueKind == JsonValueKind.Object ? m : default;
                return new Memory(
                    Id: e.GetProperty("id").GetString()!,
                    Text: e.GetProperty("memory").GetString() ?? "",
                    Project: Str(metadata, "project"),
                    Kind: Str(metadata, "kind"),
                    AgentId: Str(e, "agent_id"),
                    CreatedAt: Str(e, "created_at") is { } c && DateTimeOffset.TryParse(c, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d) ? d : null,
                    Score: Number(semanticScore && e.TryGetProperty("score_details", out var details) ? details : e,
                        semanticScore ? "semantic_score" : "score"));
            })
            .ToList();
    }

    private static double? Number(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDouble()
            : null;

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
}

/// <summary>Erreur de l'API Mem0 ; le détail n'est pas renvoyé au client MCP.</summary>
public sealed class Mem0Exception(int status) : Exception($"Mem0 a répondu {status}.")
{
    public int Status { get; } = status;
}
