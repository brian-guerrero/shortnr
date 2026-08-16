using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Shortnr.Tests.Integration.Mcp;

/// <summary>
/// Exercises the PRD-020 streaming tools: bulk <c>import_links</c> (CSV parsing,
/// UTM appending, slug dedup, row limits, activity logging) and
/// <c>aggregate_analytics</c> (cross-link click aggregation). Both report progress
/// via MCP progress notifications when the client supplies a progress token.
/// </summary>
public class McpStreamingToolsTests : McpTestBase
{
    private const string TestKey = "snr_mcptest1234567890abcdef1234567890";

    [Fact]
    public async Task ImportLinks_ImportsCsv_AppliesUtmAndGeneratesCodes()
    {
        var owner = await SeedUserAndKeyAsync("import-owner", TestKey, ApiKeyScopes.McpWrite);
        var client = CreateAuthorizedClient(TestKey);

        var csv = """
            url,slug,utm_campaign
            https://example.com/one,slamdunk,spring
            https://example.com/two,,summer
            """;
        var response = await PostJsonRpcAsync(client, "tools/call",
            ToolCall("import_links", new { csv }));

        using var json = await ReadJsonAsync(response);
        var text = ToolText(json.RootElement.GetProperty("result"));
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        Assert.Equal(2, root.GetProperty("imported").GetInt32());
        Assert.Equal(0, root.GetProperty("failed").GetInt32());
        var links = root.GetProperty("links");
        Assert.Equal("slamdunk", links[0].GetProperty("code").GetString());
        Assert.Equal("https://example.com/one?utm_campaign=spring", links[0].GetProperty("longUrl").GetString());
        Assert.Equal("https://example.com/two?utm_campaign=summer", links[1].GetProperty("longUrl").GetString());
        Assert.False(string.IsNullOrWhiteSpace(links[1].GetProperty("code").GetString()), "Auto code was generated.");

        await WaitForActivityAsync(owner, "import_links");
    }

    [Fact]
    public async Task ImportLinks_InvalidUrls_ReportedAsFailures()
    {
        await SeedUserAndKeyAsync("import-bad", TestKey, ApiKeyScopes.McpWrite);
        var client = CreateAuthorizedClient(TestKey);

        var response = await PostJsonRpcAsync(client, "tools/call",
            ToolCall("import_links", new { csv = "https://example.com/ok\nnot-a-url\nftp://nope.com" }));

        using var json = await ReadJsonAsync(response);
        var text = ToolText(json.RootElement.GetProperty("result"));
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("imported").GetInt32());
        Assert.Equal(2, root.GetProperty("failed").GetInt32());
        var errors = root.GetProperty("errors");
        Assert.Contains(errors.EnumerateArray(), e => e.GetProperty("reason").GetString()!.Contains("absolute http(s) URL"));
        Assert.True(errors.EnumerateArray().All(e => e.GetProperty("url").GetString() != "https://example.com/ok"));
    }

    [Fact]
    public async Task ImportLinks_DuplicateSlug_FailsThatRowAndContinues()
    {
        await SeedUserAndKeyAsync("import-dup", TestKey, ApiKeyScopes.McpWrite);
        var client = CreateAuthorizedClient(TestKey);

        var response = await PostJsonRpcAsync(client, "tools/call",
            ToolCall("import_links", new { csv = "https://example.com/a,dup1\nhttps://example.com/b,dup1" }));

        using var json = await ReadJsonAsync(response);
        var text = ToolText(json.RootElement.GetProperty("result"));
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("imported").GetInt32());
        Assert.Equal(1, root.GetProperty("failed").GetInt32());
        var error = root.GetProperty("errors")[0];
        Assert.Equal("https://example.com/b", error.GetProperty("url").GetString());
        Assert.Contains("already in use", error.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task ImportLinks_UnknownDomain_ReturnsError()
    {
        await SeedUserAndKeyAsync("import-domain", TestKey, ApiKeyScopes.McpWrite);
        var client = CreateAuthorizedClient(TestKey);

        var response = await PostJsonRpcAsync(client, "tools/call",
            ToolCall("import_links", new { csv = "https://example.com/a", domain = "nope.example" }));

        using var json = await ReadJsonAsync(response);
        var text = ToolText(json.RootElement.GetProperty("result"));
        Assert.Contains("not a verified domain", text);
    }

    [Fact]
    public async Task ImportLinks_OverRowLimit_ReturnsError()
    {
        await SeedUserAndKeyAsync("import-limit", TestKey, ApiKeyScopes.McpWrite);
        var client = CreateAuthorizedClient(TestKey);

        var csv = string.Join("\n", Enumerable.Range(0, 1001).Select(i => $"https://example.com/{i}"));
        var response = await PostJsonRpcAsync(client, "tools/call",
            ToolCall("import_links", new { csv }));

        using var json = await ReadJsonAsync(response);
        var text = ToolText(json.RootElement.GetProperty("result"));
        Assert.Contains("at most 1000", text);
    }

    [Fact]
    public async Task ImportLinks_RequiresMcpWriteScope()
    {
        await SeedUserAndKeyAsync("import-readonly", TestKey, ApiKeyScopes.McpRead);
        var client = CreateAuthorizedClient(TestKey);

        var response = await PostJsonRpcAsync(client, "tools/call",
            ToolCall("import_links", new { csv = "https://example.com/a" }));

        using var json = await ReadJsonAsync(response);
        var text = ToolText(json.RootElement.GetProperty("result"));
        Assert.Contains(ApiKeyScopes.McpWrite, text);
    }

    [Fact]
    public async Task AggregateAnalytics_ReturnsTotalsAndTopBreakdown()
    {
        var owner = await SeedUserAndKeyAsync("agg-owner", TestKey, ApiKeyScopes.McpRead);
        var one = await SeedLinkAsync(owner, "aaaaaa", "https://example.com/a");
        var two = await SeedLinkAsync(owner, "bbbbbb", "https://example.com/b");
        await SeedClickAsync(one, referer: "https://ref.example/a", country: "United States", device: "Desktop", browser: "Chrome");
        await SeedClickAsync(one, referer: "https://ref.example/a", country: "United States", device: "Desktop", browser: "Chrome");
        await SeedClickAsync(two, referer: "https://ref.example/b", country: "Germany", device: "Mobile", browser: "Safari");
        var client = CreateAuthorizedClient(TestKey);

        var response = await PostJsonRpcAsync(client, "tools/call", """{"name":"aggregate_analytics","arguments":{}}""");

        using var json = await ReadJsonAsync(response);
        var text = ToolText(json.RootElement.GetProperty("result"));
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        Assert.Equal(3, root.GetProperty("totalClicks").GetInt64());
        Assert.Equal(2, root.GetProperty("linksWithClicks").GetInt64());
        Assert.Equal("aaaaaa", root.GetProperty("topLinks")[0].GetProperty("code").GetString());
        Assert.Equal("https://ref.example/a", root.GetProperty("referrers")[0].GetProperty("name").GetString());
        Assert.Equal(2, root.GetProperty("referrers")[0].GetProperty("count").GetInt32());
        Assert.Equal("United States", root.GetProperty("countries")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task AggregateAnalytics_InvalidDate_ReturnsError()
    {
        await SeedUserAndKeyAsync("agg-baddate", TestKey, ApiKeyScopes.McpRead);
        var client = CreateAuthorizedClient(TestKey);

        var response = await PostJsonRpcAsync(client, "tools/call",
            """{"name":"aggregate_analytics","arguments":{"from":"bogus"}}""");

        using var json = await ReadJsonAsync(response);
        var text = ToolText(json.RootElement.GetProperty("result"));
        Assert.Contains("yyyy-MM-dd", text);
    }

    [Fact]
    public async Task AggregateAnalytics_RequiresMcpReadScope()
    {
        await SeedUserAndKeyAsync("agg-writeonly", TestKey, ApiKeyScopes.McpWrite);
        var client = CreateAuthorizedClient(TestKey);

        var response = await PostJsonRpcAsync(client, "tools/call", """{"name":"aggregate_analytics","arguments":{}}""");

        using var json = await ReadJsonAsync(response);
        var text = ToolText(json.RootElement.GetProperty("result"));
        Assert.Contains(ApiKeyScopes.McpRead, text);
    }

    private static string ToolCall(string name, object? args) =>
        JsonSerializer.Serialize(new { name, arguments = args ?? new { } });
}