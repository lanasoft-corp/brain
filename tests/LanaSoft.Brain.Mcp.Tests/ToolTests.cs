using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace LanaSoft.Brain.Mcp.Tests;

/// <summary>Les outils, appelés par le client MCP du SDK comme le ferait Claude Code.</summary>
public sealed class ToolTests(BrainMcpFactory factory) : IClassFixture<BrainMcpFactory>
{
    private async Task<McpClient> Connect(string token)
    {
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(factory.Server.BaseAddress, "/mcp"), TransportMode = HttpTransportMode.StreamableHttp },
            factory.ClientWith(token),
            ownsHttpClient: true);
        return await McpClient.CreateAsync(transport);
    }

    private static string Text(CallToolResult result) =>
        string.Concat(result.Content.OfType<TextContentBlock>().Select(t => t.Text));

    [Fact]
    public async Task Remember_then_recall_returns_the_fact()
    {
        await using var client = await Connect(BrainMcpFactory.Token());

        var added = await client.CallToolAsync("remember", new Dictionary<string, object?>
        {
            ["fact"] = "Le NAS de HQ est sauvegardé chaque nuit vers Backblaze B2.",
            ["project"] = "infra",
            ["kind"] = "fait",
        });
        Assert.NotEqual(true, added.IsError);

        var found = await client.CallToolAsync("recall", new Dictionary<string, object?> { ["query"] = "sauvegardé Backblaze", ["project"] = "infra" });
        Assert.NotEqual(true, found.IsError);
        Assert.Contains("Backblaze B2", Text(found));
    }

    [Fact]
    public async Task Identity_comes_from_the_token_not_the_arguments()
    {
        await using var client = await Connect(BrainMcpFactory.Token(azp: "copilot"));

        await client.CallToolAsync("remember", new Dictionary<string, object?>
        {
            ["fact"] = "Test d'identité.", ["project"] = "brain", ["user_id"] = "quelqu-un-d-autre",
        });

        var request = factory.Mem0.AddRequests.Last();
        Assert.Equal(BrainMcpFactory.Owner, request["user_id"]!.GetValue<string>());
        Assert.Equal("copilot", request["agent_id"]!.GetValue<string>());
        Assert.False(request["infer"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Agent_token_writes_for_the_configured_owner()
    {
        await using var client = await Connect(BrainMcpFactory.Token(username: "service-account-agent-trading", azp: "agent-trading"));

        await client.CallToolAsync("remember", new Dictionary<string, object?> { ["fact"] = "Fait d'agent.", ["project"] = "trading" });

        var request = factory.Mem0.AddRequests.Last();
        Assert.Equal(BrainMcpFactory.Owner, request["user_id"]!.GetValue<string>());
        Assert.Equal("agent-trading", request["agent_id"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("https://claude.ai/oauth/claude-code-client-metadata", "claude-code")]
    [InlineData("https://inconnu.example/client-metadata", "https://inconnu.example/client-metadata")]
    public async Task Url_client_ids_are_replaced_by_their_alias(string azp, string expected)
    {
        await using var client = await Connect(BrainMcpFactory.Token(azp: azp));

        await client.CallToolAsync("remember", new Dictionary<string, object?> { ["fact"] = "Fait d'alias.", ["project"] = "brain" });

        Assert.Equal(expected, factory.Mem0.AddRequests.Last()["agent_id"]!.GetValue<string>());
    }

    [Fact]
    public async Task Session_summary_asks_mem0_to_extract_facts()
    {
        await using var client = await Connect(BrainMcpFactory.Token());

        await client.CallToolAsync("session_summary", new Dictionary<string, object?>
        {
            ["summary"] = "On a choisi Gemini parce que le cluster manque de RAM.", ["project"] = "brain", ["run_id"] = "session-1",
        });

        var request = factory.Mem0.AddRequests.Last();
        Assert.True(request["infer"]!.GetValue<bool>());
        Assert.Equal("resume", request["metadata"]!["kind"]!.GetValue<string>());
        Assert.Equal("session-1", request["run_id"]!.GetValue<string>());
    }

    [Fact]
    public async Task Session_summary_returns_the_related_facts_of_the_project()
    {
        await using var me = await Connect(BrainMcpFactory.Token(username: "resumeur"));
        var old = await Remember(me, "Le service de facturation tourne sur trois instances.", "orion");
        var elsewhere = await Remember(me, "Le service de facturation tourne sur trois instances.", "ailleurs");
        var unrelated = await Remember(me, "Les journaux partent vers Loki.", "orion");

        var result = await me.CallToolAsync("session_summary", new Dictionary<string, object?>
        {
            ["summary"] = "Le service de facturation tourne sur cinq instances.", ["project"] = "orion",
        });

        Assert.NotEqual(true, result.IsError);
        var fact = Assert.Single(JsonDocument.Parse(Text(result)).RootElement.EnumerateArray());
        Assert.Contains("cinq instances", fact.GetProperty("fact").GetProperty("text").GetString());
        var related = fact.GetProperty("related").EnumerateArray().Select(r => r.GetProperty("id").GetString()).ToList();
        Assert.Equal([old], related);
        Assert.DoesNotContain(elsewhere, related);
        Assert.DoesNotContain(unrelated, related);
    }

    [Fact]
    public async Task Session_summary_keeps_the_facts_when_the_related_search_fails()
    {
        await using var me = await Connect(BrainMcpFactory.Token(username: "resumeur-en-panne"));

        factory.Mem0.FailSearches = true;
        CallToolResult result;
        try
        {
            result = await me.CallToolAsync("session_summary", new Dictionary<string, object?>
            {
                ["summary"] = "Le cache passe à Redis.", ["project"] = "orion",
            });
        }
        finally { factory.Mem0.FailSearches = false; }

        Assert.NotEqual(true, result.IsError);
        var fact = Assert.Single(JsonDocument.Parse(Text(result)).RootElement.EnumerateArray());
        Assert.Empty(fact.GetProperty("related").EnumerateArray());
    }

    [Fact]
    public async Task Write_tools_require_the_write_scope()
    {
        await using var client = await Connect(BrainMcpFactory.Token(scope: "brain"));

        var tools = (await client.ListToolsAsync()).Select(t => t.Name).ToList();
        Assert.Contains("recall", tools);
        Assert.DoesNotContain("remember", tools);
        Assert.DoesNotContain("rename_project", tools);

        await Assert.ThrowsAnyAsync<McpException>(() => client.CallToolAsync(
            "remember", new Dictionary<string, object?> { ["fact"] = "Interdit.", ["project"] = "brain" }).AsTask());
    }

    [Theory]
    [InlineData("Mon IBAN est FR76 3000 6000 0112 3456 7890 189.")]
    [InlineData("La clé Anthropic est sk-ant-api03-abcdefghijklmnopqrstuvwxyz0123456789.")]
    [InlineData("mot de passe : Sup3rS3cret!")]
    [InlineData("Jeton eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.abcdefghijklmnop")] // gitleaks:allow (faux jeton de test)
    [InlineData("Carte 4111 1111 1111 1111")]
    public async Task Secrets_are_refused(string fact)
    {
        await using var client = await Connect(BrainMcpFactory.Token());
        var before = factory.Mem0.AddRequests.Count;

        var result = await client.CallToolAsync("remember", new Dictionary<string, object?> { ["fact"] = fact, ["project"] = "perso" });

        Assert.True(result.IsError);
        Assert.Contains("secret", Text(result));
        Assert.Equal(before, factory.Mem0.AddRequests.Count);
    }

    [Theory]
    [InlineData("Le cluster de démo a 7 nœuds, Traefik écoute sur 192.0.2.40.")]
    [InlineData("L'image mem0-api est figée sur sha256:815eb51055749f6749025b66843060c5997a6274aad9e734d6a7db3976205f00.")]
    [InlineData("Le plafond journalier d'achat est de 300 euros, commande 202609271234.")]
    [InlineData("Le mot de passe de breakglass est rangé dans le coffre sous admin-breakglass-password.")]
    public async Task Ordinary_facts_are_accepted(string fact)
    {
        await using var client = await Connect(BrainMcpFactory.Token());
        var result = await client.CallToolAsync("remember", new Dictionary<string, object?> { ["fact"] = fact, ["project"] = "infra" });
        Assert.NotEqual(true, result.IsError);
    }

    [Fact]
    public async Task Forget_cannot_delete_someone_elses_memory()
    {
        await using var owner = await Connect(BrainMcpFactory.Token(username: "autre"));
        var added = await owner.CallToolAsync("remember", new Dictionary<string, object?> { ["fact"] = "Mémoire d'autre.", ["project"] = "perso" });
        var id = JsonDocument.Parse(Text(added)).RootElement[0].GetProperty("id").GetString();

        await using var me = await Connect(BrainMcpFactory.Token());
        var result = await me.CallToolAsync("forget", new Dictionary<string, object?> { ["memory_id"] = id });

        Assert.True(result.IsError);
        Assert.Contains(factory.Mem0.Memories, m => m["id"]!.GetValue<string>() == id);
    }
    private static async Task<string> Remember(McpClient client, string fact, string project, string kind = "fait")
    {
        var added = await client.CallToolAsync("remember", new Dictionary<string, object?> { ["fact"] = fact, ["project"] = project, ["kind"] = kind });
        return JsonDocument.Parse(Text(added)).RootElement[0].GetProperty("id").GetString()!;
    }

    private JsonObject Stored(string id) => factory.Mem0.Memories.Single(m => m["id"]!.GetValue<string>() == id);

    [Fact]
    public async Task Rename_project_moves_the_memories_without_rewriting_them()
    {
        await using var me = await Connect(BrainMcpFactory.Token(username: "renommeur"));
        await using var other = await Connect(BrainMcpFactory.Token(username: "voisin"));
        var first = await Remember(me, "Premier fait.", "ancien-nom", "decision");
        var second = await Remember(me, "Second fait.", "Ancien-Nom");
        var kept = await Remember(me, "Autre projet.", "ailleurs");
        var foreign = await Remember(other, "Fait du voisin.", "ancien-nom");
        var before = Stored(first).DeepClone();

        var result = await me.CallToolAsync("rename_project", new Dictionary<string, object?> { ["from"] = "ancien-nom", ["to"] = "nouveau-nom" });

        Assert.NotEqual(true, result.IsError);
        Assert.Contains("2 mémoire(s)", Text(result));
        var after = Stored(first);
        Assert.Equal("nouveau-nom", after["metadata"]!["project"]!.GetValue<string>());
        Assert.Equal("decision", after["metadata"]!["kind"]!.GetValue<string>());
        Assert.Equal(before["memory"]!.GetValue<string>(), after["memory"]!.GetValue<string>());
        Assert.Equal(before["created_at"]!.GetValue<string>(), after["created_at"]!.GetValue<string>());
        Assert.Equal("nouveau-nom", Stored(second)["metadata"]!["project"]!.GetValue<string>());
        Assert.Equal("ailleurs", Stored(kept)["metadata"]!["project"]!.GetValue<string>());
        Assert.Equal("ancien-nom", Stored(foreign)["metadata"]!["project"]!.GetValue<string>());
    }

    [Fact]
    public async Task Rename_project_can_be_resumed_after_an_interruption()
    {
        await using var me = await Connect(BrainMcpFactory.Token(username: "interrompu"));
        var ids = new List<string>();
        for (var i = 0; i < 3; i++) ids.Add(await Remember(me, $"Fait {i}.", "a-renommer"));
        var args = new Dictionary<string, object?> { ["from"] = "a-renommer", ["to"] = "renomme" };

        factory.Mem0.FailUpdatesAfter = 1;
        CallToolResult failed;
        try { failed = await me.CallToolAsync("rename_project", args); }
        finally { factory.Mem0.FailUpdatesAfter = -1; }
        var resumed = await me.CallToolAsync("rename_project", args);

        Assert.True(failed.IsError);
        Assert.Contains("1 mémoire(s) sur 3", Text(failed));
        Assert.NotEqual(true, resumed.IsError);
        Assert.Contains("2 mémoire(s)", Text(resumed));
        Assert.All(ids, id => Assert.Equal("renomme", Stored(id)["metadata"]!["project"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Rename_project_rejects_an_unknown_project()
    {
        await using var me = await Connect(BrainMcpFactory.Token(username: "sans-projet"));

        var result = await me.CallToolAsync("rename_project", new Dictionary<string, object?> { ["from"] = "inexistant", ["to"] = "autre" });

        Assert.True(result.IsError);
    }
}
