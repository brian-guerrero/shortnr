using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Shortnr.Tests.Integration.Mcp;

/// <summary>
/// Verifies the MCP endpoint wiring: authentication via the ApiKey scheme, the
/// "mcp" policy (at least one mcp:* scope), and JSON-RPC dispatch in stateless
/// mode. Individual tools and their read/write scope enforcement are covered by
/// the tool tests added with the read/write tool layers.
/// </summary>
public class McpInfrastructureTests : McpTestBase
{
    private const string TestKey = "snr_mcptest1234567890abcdef1234567890";

    [Fact]
    public async Task McpEndpoint_NoAuth_Returns401()
    {
        var client = Factory.CreateClient();
        var response = await PostJsonRpcAsync(client, "tools/list", "{}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task McpEndpoint_KeyWithoutMcpScope_Returns403()
    {
        await SeedUserAndKeyAsync("rest-only", TestKey, ApiKeyScopes.LinksRead);
        var client = CreateAuthorizedClient(TestKey);

        var response = await PostJsonRpcAsync(client, "tools/list", "{}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task McpEndpoint_ReadKey_ToolsListReturnsPing()
    {
        await SeedUserAndKeyAsync("mcp-read", TestKey, ApiKeyScopes.McpRead);
        var client = CreateAuthorizedClient(TestKey);

        var response = await PostJsonRpcAsync(client, "tools/list", "{}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ReadJsonAsync(response);
        var tools = json.RootElement.GetProperty("result").GetProperty("tools");
        var names = tools.EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToArray();
        Assert.Contains("ping", names);
        Assert.Equal(16, tools.GetArrayLength());
    }

    [Fact]
    public async Task McpEndpoint_CallPing_ReturnsPong()
    {
        await SeedUserAndKeyAsync("mcp-read", TestKey, ApiKeyScopes.McpRead);
        var client = CreateAuthorizedClient(TestKey);

        var response = await PostJsonRpcAsync(client, "tools/call", """{"name":"ping","arguments":{}}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ReadJsonAsync(response);
        var text = ToolText(json.RootElement.GetProperty("result"));
        Assert.Equal("pong", text);
    }

    [Fact]
    public async Task McpEndpoint_LegacyKey_HasMcpAccess()
    {
        await SeedUserAndKeyAsync("legacy", TestKey, "");
        var client = CreateAuthorizedClient(TestKey);

        var response = await PostJsonRpcAsync(client, "tools/list", "{}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task McpEndpoint_UnknownTool_ReturnsJsonRpcError()
    {
        await SeedUserAndKeyAsync("mcp-read", TestKey, ApiKeyScopes.McpRead);
        var client = CreateAuthorizedClient(TestKey);

        var response = await PostJsonRpcAsync(client, "tools/call", """{"name":"no_such_tool","arguments":{}}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ReadJsonAsync(response);
        Assert.True(json.RootElement.TryGetProperty("error", out _), "Expected a JSON-RPC error for an unknown tool.");
    }
}
