using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Shortnr.Tests.Integration.Mcp;

/// <summary>
/// Exercises the read-only MCP tools and the bio resource: scope enforcement,
/// data scoping to the calling key's owner, filters/sorts, and JSON shapes.
/// </summary>
public class McpReadToolsTests : McpTestBase
{
    private const string TestKey = "snr_mcptest1234567890abcdef1234567890";

    [Fact]
    public async Task ReadTools_KeyWithoutMcpReadScope_ReturnsScopeError()
    {
        await SeedUserAndKeyAsync("write-only", TestKey, ApiKeyScopes.McpWrite);
        var client = CreateAuthorizedClient(TestKey);

        var response = await PostJsonRpcAsync(client, "tools/call", """{"name":"list_links","arguments":{}}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ReadJsonAsync(response);
        var text = ToolText(json.RootElement.GetProperty("result"));
        Assert.Contains(ApiKeyScopes.McpRead, text);
    }

    [Fact]
    public async Task ListLinks_ReturnsOnlyOwnedLinks_SortedByClicks()
    {
        var owner = await SeedUserAndKeyAsync("list-owner", TestKey, ApiKeyScopes.McpRead);
        await SeedLinkAsync(owner, "aaaaaa", "https://example.com/one", clicks: 1);
        await SeedLinkAsync(owner, "bbbbbb", "https://example.com/two", clicks: 3);
        await SeedLinkAsync(await SeedUserAsync("other"), "cccccc", "https://other.example/secret", clicks: 99);

        var client = CreateAuthorizedClient(TestKey);
        var response = await PostJsonRpcAsync(client, "tools/call",
            """{"name":"list_links","arguments":{"sort":"clicks_desc"}}""");

        using var json = await ReadJsonAsync(response);
        var links = json.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString();
        using var doc = JsonDocument.Parse(links!);
        var array = doc.RootElement;
        Assert.Equal(2, array.GetArrayLength());
        Assert.Equal("bbbbbb", array[0].GetProperty("shortCode").GetString());
        Assert.Equal(3, array[0].GetProperty("clickCount").GetInt64());
        Assert.Equal("aaaaaa", array[1].GetProperty("shortCode").GetString());
        Assert.Equal("https://example.com/one", array[1].GetProperty("longUrl").GetString());
    }

    [Fact]
    public async Task ListLinks_FiltersByDomainDefault()
    {
        var owner = await SeedUserAndKeyAsync("domain-owner", TestKey, ApiKeyScopes.McpRead);
        await SeedLinkAsync(owner, "aaaaaa", "https://example.com/a");
        await SeedLinkAsync(owner, "bbbbbb", "https://example.com/b");
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Shortnr.Data.AppDbContext>();
            var domain = new Shortnr.Data.Entities.Domain
            {
                OwnerUserId = owner,
                Hostname = "links.example",
                IsVerified = true,
                VerificationToken = "tok"
            };
            db.Domains.Add(domain);
            await db.SaveChangesAsync();
            db.ShortenedUrls.Add(new Shortnr.Data.Entities.ShortenedUrl
            {
                OwnerUserId = owner,
                ShortCode = "cccccc",
                LongUrl = "https://example.com/c",
                DomainId = domain.Id,
                ClickCount = 0,
                CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var client = CreateAuthorizedClient(TestKey);
        var response = await PostJsonRpcAsync(client, "tools/call",
            """{"name":"list_links","arguments":{"domain":"default"}}""");

        using var json = await ReadJsonAsync(response);
        var links = ToolText(json.RootElement.GetProperty("result"));
        using var doc = JsonDocument.Parse(links);
        Assert.Equal(2, doc.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task ListLinks_NoResults_ReturnsMessage()
    {
        await SeedUserAndKeyAsync("empty-owner", TestKey, ApiKeyScopes.McpRead);
        var client = CreateAuthorizedClient(TestKey);

        var response = await PostJsonRpcAsync(client, "tools/call", """{"name":"list_links","arguments":{}}""");

        using var json = await ReadJsonAsync(response);
        var text = ToolText(json.RootElement.GetProperty("result"));
        Assert.Equal("No links found.", text);
    }

    [Fact]
    public async Task ListLinks_InvalidSort_ReturnsError()
    {
        var owner = await SeedUserAndKeyAsync("sort-owner", TestKey, ApiKeyScopes.McpRead);
        await SeedLinkAsync(owner, "aaaaaa", "https://example.com/a");
        var client = CreateAuthorizedClient(TestKey);

        var response = await PostJsonRpcAsync(client, "tools/call",
            """{"name":"list_links","arguments":{"sort":"bogus"}}""");

        using var json = await ReadJsonAsync(response);
        var text = ToolText(json.RootElement.GetProperty("result"));
        Assert.Contains("invalid sort", text);
    }

    [Fact]
    public async Task ListLinks_FiltersByCampaign()
    {
        var owner = await SeedUserAndKeyAsync("campaign-owner", TestKey, ApiKeyScopes.McpRead);
        var springLink = await SeedLinkAsync(owner, "spring1", "https://example.com/spring");
        await SeedMetadataAsync(springLink, utmCampaign: "spring-sale-2026");
        var summerLink = await SeedLinkAsync(owner, "summer1", "https://example.com/summer");
        await SeedMetadataAsync(summerLink, utmCampaign: "summer-sale-2026");
        await SeedLinkAsync(owner, "plain11", "https://example.com/plain");

        var client = CreateAuthorizedClient(TestKey);
        var response = await PostJsonRpcAsync(client, "tools/call",
            """{"name":"list_links","arguments":{"campaign":"spring"}}""");

        using var json = await ReadJsonAsync(response);
        var text = ToolText(json.RootElement.GetProperty("result"));
        using var doc = JsonDocument.Parse(text);
        Assert.Equal(1, doc.RootElement.GetArrayLength());
        Assert.Equal("spring1", doc.RootElement[0].GetProperty("shortCode").GetString());
        Assert.Equal("spring-sale-2026", doc.RootElement[0].GetProperty("metadata").GetProperty("utmCampaign").GetString());
    }

    [Fact]
    public async Task ListPixelSnippets_ReturnsSeededSnippets()
    {
        await SeedUserAndKeyAsync("pixel-owner", TestKey, ApiKeyScopes.McpRead);
        var client = CreateAuthorizedClient(TestKey);

        var response = await PostJsonRpcAsync(client, "tools/call",
            """{"name":"list_pixel_snippets","arguments":{}}""");

        using var json = await ReadJsonAsync(response);
        var text = ToolText(json.RootElement.GetProperty("result"));
        using var doc = JsonDocument.Parse(text);
        var names = doc.RootElement.EnumerateArray().Select(e => e.GetProperty("name").GetString()).ToList();
        Assert.Contains("Meta Pixel", names);
        Assert.Contains("Google Ads", names);
        Assert.Contains("Custom snippet", names);
        var custom = doc.RootElement.EnumerateArray().Single(e => e.GetProperty("name").GetString() == "Custom snippet");
        Assert.True(custom.GetProperty("isCustom").GetBoolean());
    }

    [Fact]
    public async Task GetLinkStats_IncludesCampaignMetadata()
    {
        var owner = await SeedUserAndKeyAsync("stats-owner", TestKey, ApiKeyScopes.McpRead);
        var linkId = await SeedLinkAsync(owner, "stats2", "https://example.com/stats2");
        await SeedMetadataAsync(linkId, utmCampaign: "fall-sale", pixelSnippetId: 1, pixelId: "555");

        var client = CreateAuthorizedClient(TestKey);
        var response = await PostJsonRpcAsync(client, "tools/call",
            """{"name":"get_link_stats","arguments":{"short_code":"stats2"}}""");

        using var json = await ReadJsonAsync(response);
        var text = ToolText(json.RootElement.GetProperty("result"));
        using var doc = JsonDocument.Parse(text);
        var metadata = doc.RootElement.GetProperty("metadata");
        Assert.Equal("fall-sale", metadata.GetProperty("utmCampaign").GetString());
        Assert.Equal("Meta Pixel", metadata.GetProperty("pixelSnippet").GetString());
        Assert.Equal("555", metadata.GetProperty("pixelValue").GetString());
    }

    [Fact]
    public async Task GetLinkStats_ReturnsAggregates()
    {
        var owner = await SeedUserAndKeyAsync("stats-owner", TestKey, ApiKeyScopes.McpRead);
        var linkId = await SeedLinkAsync(owner, "stats1", "https://example.com/stats");
        await SeedClickAsync(linkId, referer: "https://ref.example/a", country: "United States", device: "Desktop", browser: "Chrome");
        await SeedClickAsync(linkId, referer: "https://ref.example/a", country: "United States", device: "Desktop", browser: "Chrome");
        await SeedClickAsync(linkId, referer: "https://ref.example/b", country: "Germany", device: "Mobile", browser: "Safari");

        var client = CreateAuthorizedClient(TestKey);
        var response = await PostJsonRpcAsync(client, "tools/call",
            """{"name":"get_link_stats","arguments":{"short_code":"stats1"}}""");

        using var json = await ReadJsonAsync(response);
        var text = ToolText(json.RootElement.GetProperty("result"));
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        Assert.Equal("stats1", root.GetProperty("shortCode").GetString());
        Assert.Equal(3, root.GetProperty("clickCount").GetInt64());
        var referrers = root.GetProperty("topReferrers");
        Assert.Equal("https://ref.example/a", referrers[0].GetProperty("name").GetString());
        Assert.Equal(2, referrers[0].GetProperty("count").GetInt32());
        var countries = root.GetProperty("topCountries");
        Assert.Equal("United States", countries[0].GetProperty("name").GetString());
        Assert.Equal(2, countries[0].GetProperty("count").GetInt32());
        Assert.Equal("Chrome", root.GetProperty("topBrowsers")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task GetLinkStats_UnknownCode_ReturnsError()
    {
        await SeedUserAndKeyAsync("stats-owner", TestKey, ApiKeyScopes.McpRead);
        var client = CreateAuthorizedClient(TestKey);

        var response = await PostJsonRpcAsync(client, "tools/call",
            """{"name":"get_link_stats","arguments":{"short_code":"nope1"}}""");

        using var json = await ReadJsonAsync(response);
        var text = ToolText(json.RootElement.GetProperty("result"));
        Assert.Contains("no link", text);
    }

    [Fact]
    public async Task GetTopLinks_RespectsPeriodAndLimit()
    {
        var owner = await SeedUserAndKeyAsync("top-owner", TestKey, ApiKeyScopes.McpRead);
        var old = DateTime.UtcNow.AddDays(-10);
        var recent = DateTime.UtcNow.AddDays(-1);
        var a = await SeedLinkAsync(owner, "aaaaaa", "https://example.com/a");
        var b = await SeedLinkAsync(owner, "bbbbbb", "https://example.com/b");
        var c = await SeedLinkAsync(owner, "cccccc", "https://example.com/c");
        for (var i = 0; i < 3; i++) await SeedClickAsync(a, at: old);
        for (var i = 0; i < 2; i++) await SeedClickAsync(b, at: recent);
        await SeedClickAsync(c, at: recent);

        var client = CreateAuthorizedClient(TestKey);
        var all = await PostJsonRpcAsync(client, "tools/call",
            """{"name":"get_top_links","arguments":{"period":"all"}}""");
        using (var json = await ReadJsonAsync(all))
        {
            var text = ToolText(json.RootElement.GetProperty("result"));
            using var doc = JsonDocument.Parse(text);
            Assert.Equal("aaaaaa", doc.RootElement[0].GetProperty("shortCode").GetString());
            Assert.Equal(3, doc.RootElement[0].GetProperty("clickCount").GetInt64());
        }

        var recentOnly = await PostJsonRpcAsync(client, "tools/call",
            """{"name":"get_top_links","arguments":{"period":"7d"}}""");
        using (var json = await ReadJsonAsync(recentOnly))
        {
            var text = ToolText(json.RootElement.GetProperty("result"));
            using var doc = JsonDocument.Parse(text);
            Assert.Equal(2, doc.RootElement.GetArrayLength());
            Assert.Equal("bbbbbb", doc.RootElement[0].GetProperty("shortCode").GetString());
        }
    }

    [Fact]
    public async Task ListBioPageLinks_ReturnsPageState()
    {
        var owner = await SeedUserAndKeyAsync("bio-owner", TestKey, ApiKeyScopes.McpRead);
        var pageId = await SeedBioPageAsync(owner, "jane", "Jane Doe", theme: "dark");
        var one = await SeedLinkAsync(owner, "aaaaaa", "https://example.com/one");
        var two = await SeedLinkAsync(owner, "bbbbbb", "https://example.com/two");
        await AddBioPageLinkAsync(pageId, one, "Site", sortOrder: 0);
        await AddBioPageLinkAsync(pageId, two, "Social", sortOrder: 1);

        var client = CreateAuthorizedClient(TestKey);
        var response = await PostJsonRpcAsync(client, "tools/call", """{"name":"list_bio_page_links","arguments":{}}""");

        using var json = await ReadJsonAsync(response);
        var text = ToolText(json.RootElement.GetProperty("result"));
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        Assert.Equal("jane", root.GetProperty("slug").GetString());
        Assert.Equal("Jane Doe", root.GetProperty("displayName").GetString());
        Assert.Equal("dark", root.GetProperty("theme").GetString());
        var links = root.GetProperty("links");
        Assert.Equal(2, links.GetArrayLength());
        Assert.Equal("aaaaaa", links[0].GetProperty("shortCode").GetString());
        Assert.Equal(1, links[0].GetProperty("position").GetInt32());
        Assert.Equal("Social", links[1].GetProperty("title").GetString());
    }

    [Fact]
    public async Task ListBioPageLinks_NoPage_ReturnsMessage()
    {
        await SeedUserAndKeyAsync("bio-empty", TestKey, ApiKeyScopes.McpRead);
        var client = CreateAuthorizedClient(TestKey);

        var response = await PostJsonRpcAsync(client, "tools/call", """{"name":"list_bio_page_links","arguments":{}}""");

        using var json = await ReadJsonAsync(response);
        var text = ToolText(json.RootElement.GetProperty("result"));
        Assert.Contains("No bio page exists yet", text);
    }

    [Fact]
    public async Task BioPageResource_Read_ReturnsBioState()
    {
        var owner = await SeedUserAndKeyAsync("bio-res", TestKey, ApiKeyScopes.McpRead);
        var pageId = await SeedBioPageAsync(owner, "res", "Res Owner");
        var link = await SeedLinkAsync(owner, "aaaaaa", "https://example.com/a");
        await AddBioPageLinkAsync(pageId, link, "Link", sortOrder: 0);

        var client = CreateAuthorizedClient(TestKey);
        var response = await PostJsonRpcAsync(client, "resources/read", """{"uri":"shortnr://bio"}""");

        using var json = await ReadJsonAsync(response);
        var content = json.RootElement.GetProperty("result").GetProperty("contents")[0];
        Assert.Equal("application/json", content.GetProperty("mimeType").GetString());
        var text = content.GetProperty("text").GetString();
        using var doc = JsonDocument.Parse(text!);
        Assert.Equal("res", doc.RootElement.GetProperty("slug").GetString());
        Assert.Equal("aaaaaa", doc.RootElement.GetProperty("links")[0].GetProperty("shortCode").GetString());
    }

    [Fact]
    public async Task BioPageResource_NoPage_ReturnsError()
    {
        await SeedUserAndKeyAsync("bio-res-empty", TestKey, ApiKeyScopes.McpRead);
        var client = CreateAuthorizedClient(TestKey);

        var response = await PostJsonRpcAsync(client, "resources/read", """{"uri":"shortnr://bio"}""");

        using var json = await ReadJsonAsync(response);
        var text = json.RootElement.GetProperty("result").GetProperty("contents")[0].GetProperty("text").GetString();
        Assert.Contains("No bio page exists yet", text);
    }
}
