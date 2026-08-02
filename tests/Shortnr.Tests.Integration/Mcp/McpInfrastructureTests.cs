using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Tests.Integration.Infrastructure;
using Shortnr.Web.Services;

namespace Shortnr.Tests.Integration.Mcp;

/// <summary>
/// Verifies the MCP endpoint wiring: authentication via the ApiKey scheme, the
/// "mcp" policy (at least one mcp:* scope), and JSON-RPC dispatch in stateless
/// mode. Individual tools and their read/write scope enforcement are covered by
/// the tool tests added with the read/write tool layers.
/// </summary>
public class McpInfrastructureTests : IAsyncLifetime
{
    private const string ProtocolVersion = "2025-06-18";
    private const string TestKey = "snr_mcptest1234567890abcdef1234567890";

    private readonly ShortnrWebAppFactory _factory = new(authEnabled: true);

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    [Fact]
    public async Task McpEndpoint_NoAuth_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await PostJsonRpcAsync(client, "tools/list", "{}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task McpEndpoint_KeyWithoutMcpScope_Returns403()
    {
        await SeedUserAndKeyAsync("rest-only", TestKey, ApiKeyScopes.LinksRead);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestKey);

        var response = await PostJsonRpcAsync(client, "tools/list", "{}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task McpEndpoint_ReadKey_ToolsListReturnsPing()
    {
        await SeedUserAndKeyAsync("mcp-read", TestKey, ApiKeyScopes.McpRead);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestKey);

        var response = await PostJsonRpcAsync(client, "tools/list", "{}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ReadJsonAsync(response);
        var tools = json.RootElement.GetProperty("result").GetProperty("tools");
        var tool = Assert.Single(tools.EnumerateArray());
        Assert.Equal("ping", tool.GetProperty("name").GetString());
    }

    [Fact]
    public async Task McpEndpoint_CallPing_ReturnsPong()
    {
        await SeedUserAndKeyAsync("mcp-read", TestKey, ApiKeyScopes.McpRead);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestKey);

        var response = await PostJsonRpcAsync(client, "tools/call", """{"name":"ping","arguments":{}}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ReadJsonAsync(response);
        var content = json.RootElement.GetProperty("result").GetProperty("content");
        var text = content[0].GetProperty("text").GetString();
        Assert.Equal("pong", text);
    }

    [Fact]
    public async Task McpEndpoint_LegacyKey_HasMcpAccess()
    {
        await SeedUserAndKeyAsync("legacy", TestKey, "");
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestKey);

        var response = await PostJsonRpcAsync(client, "tools/list", "{}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task McpEndpoint_UnknownTool_ReturnsJsonRpcError()
    {
        await SeedUserAndKeyAsync("mcp-read", TestKey, ApiKeyScopes.McpRead);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestKey);

        var response = await PostJsonRpcAsync(client, "tools/call", """{"name":"no_such_tool","arguments":{}}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ReadJsonAsync(response);
        Assert.True(json.RootElement.TryGetProperty("error", out _), "Expected a JSON-RPC error for an unknown tool.");
    }

    private async Task<long> SeedUserAndKeyAsync(string subject, string key, string scopes)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new User
        {
            Issuer = ShortnrWebAppFactory.TestIssuer,
            Subject = subject,
            CreatedAtUtc = DateTime.UtcNow,
            LastLoginAtUtc = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.ApiKeys.Add(new ApiKey
        {
            OwnerUserId = user.Id,
            KeyHash = ApiKeyService.HashKey(key),
            KeyPrefix = "snr_",
            Label = "mcp test key",
            Scopes = scopes,
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return user.Id;
    }

    private static async Task<HttpResponseMessage> PostJsonRpcAsync(HttpClient client, string method, string @params)
    {
        var body = $$"""{"jsonrpc":"2.0","id":1,"method":"{{method}}","params":{{@params}}}""";
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", ProtocolVersion);
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
        return await client.SendAsync(request);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        // Stateless streamable HTTP wraps single responses in an SSE frame even
        // when only one message is emitted; lift the JSON out of the data: lines.
        var payload = string.Join("", text.Split('\n')
            .Where(line => line.StartsWith("data:", StringComparison.Ordinal))
            .Select(line => line["data:".Length..].Trim()));
        return JsonDocument.Parse(payload.Length > 0 ? payload : text);
    }
}
