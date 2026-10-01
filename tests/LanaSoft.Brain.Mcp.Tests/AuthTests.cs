using System.Net;
using System.Text;
using System.Text.Json;

namespace LanaSoft.Brain.Mcp.Tests;

/// <summary>Authentification et autorisation sur /mcp, et métadonnées RFC 9728.</summary>
public sealed class AuthTests(BrainMcpFactory factory) : IClassFixture<BrainMcpFactory>
{
    private const string Initialize = """
        {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}
        """;

    private static HttpRequestMessage McpRequest() => new(HttpMethod.Post, "/mcp")
    {
        Content = new StringContent(Initialize, Encoding.UTF8, "application/json"),
        Headers = { { "Accept", "application/json, text/event-stream" } },
    };

    [Fact]
    public async Task Without_token_returns_401_with_resource_metadata()
    {
        var response = await factory.ClientWith(null).SendAsync(McpRequest());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var challenge = Assert.Single(response.Headers.WwwAuthenticate);
        Assert.Equal("Bearer", challenge.Scheme);
        Assert.Contains("resource_metadata=", challenge.Parameter);
        Assert.Contains("/.well-known/oauth-protected-resource", challenge.Parameter);
    }

    [Fact]
    public async Task Wrong_audience_returns_401()
    {
        var response = await factory.ClientWith(BrainMcpFactory.Token(audience: "https://ailleurs.example/mcp")).SendAsync(McpRequest());
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Wrong_issuer_returns_401()
    {
        var response = await factory.ClientWith(BrainMcpFactory.Token(issuer: "https://auth.example.test/realms/master")).SendAsync(McpRequest());
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Token_without_brain_scope_is_forbidden()
    {
        var response = await factory.ClientWith(BrainMcpFactory.Token(scope: "profile email")).SendAsync(McpRequest());
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Valid_token_initializes_the_session()
    {
        var response = await factory.ClientWith(BrainMcpFactory.Token(scope: "brain")).SendAsync(McpRequest());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Protected_resource_metadata_points_to_the_identity_provider()
    {
        var json = await factory.ClientWith(null).GetStringAsync("/.well-known/oauth-protected-resource");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(BrainMcpFactory.Audience, root.GetProperty("resource").GetString());
        Assert.Equal(BrainMcpFactory.Issuer, root.GetProperty("authorization_servers")[0].GetString());
        var scopes = root.GetProperty("scopes_supported").EnumerateArray().Select(s => s.GetString()).ToList();
        Assert.Contains("brain", scopes);
        Assert.Contains("brain:write", scopes);
    }

    [Fact]
    public async Task Health_endpoint_is_anonymous()
    {
        var response = await factory.ClientWith(null).GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
