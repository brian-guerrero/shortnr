using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shortnr.Data;
using Shortnr.Data.Entities;

namespace Shortnr.Tests.Integration.Mcp;

/// <summary>
/// Exercises the read-only MCP resources introduced by PRD-020: list/template
/// discovery, data scoping to the calling key's owner and workspaces, pagination,
/// filters, and the JSON shapes of <c>shortnr://links</c>, <c>shortnr://links/{code}</c>,
/// <c>shortnr://analytics/{code}</c> and <c>shortnr://workspaces</c>.
/// </summary>
public class McpResourceTests : McpTestBase
{
    private const string TestKey = "snr_mcptest1234567890abcdef1234567890";

    [Fact]
    public async Task ResourcesList_ExposesAllDocumentedUris()
    {
        await SeedUserAndKeyAsync("res-discover", TestKey, ApiKeyScopes.McpRead);
        var client = CreateAuthorizedClient(TestKey);

        var list = await PostJsonRpcAsync(client, "resources/list", "{}");
        using (var json = await ReadJsonAsync(list))
        {
            var uris = json.RootElement.GetProperty("result").GetProperty("resources")
                .EnumerateArray().Select(r => r.GetProperty("uri").GetString()).ToArray();
            Assert.Contains("shortnr://workspaces", uris);
            Assert.Contains("shortnr://bio", uris);
        }

        var templatesResp = await PostJsonRpcAsync(client, "resources/templates/list", "{}");
        using var templatesJson = await ReadJsonAsync(templatesResp);
        var templates = templatesJson.RootElement.GetProperty("result").GetProperty("resourceTemplates")
            .EnumerateArray().Select(r => r.GetProperty("uriTemplate").GetString()).ToArray();
        Assert.Contains(templates, t => t?.StartsWith("shortnr://links") == true);
        Assert.Contains("shortnr://links/{code}", templates);
        Assert.Contains(templates, t => t?.StartsWith("shortnr://analytics/{code}") == true);
    }

    [Fact]
    public async Task LinksResource_PaginatesNewestFirst_ScopedToOwner()
    {
        var owner = await SeedUserAndKeyAsync("res-list", TestKey, ApiKeyScopes.McpRead);
        await SeedLinkAtAsync(owner, "aaaaaa", "https://example.com/a", DateTime.UtcNow.AddMinutes(-3));
        await SeedLinkAtAsync(owner, "bbbbbb", "https://example.com/b", DateTime.UtcNow.AddMinutes(-2));
        await SeedLinkAtAsync(owner, "cccccc", "https://example.com/c", DateTime.UtcNow.AddMinutes(-1));
        await SeedLinkAtAsync(await SeedUserAsync("other"), "zzzzzz", "https://other.example/secret", DateTime.UtcNow);
        var client = CreateAuthorizedClient(TestKey);

        var response = await PostJsonRpcAsync(client, "resources/read", """{"uri":"shortnr://links"}""");
        using var json = await ReadJsonAsync(response);
        using var doc = ParseResource(json.RootElement.GetProperty("result"));
        Assert.Equal(3, doc.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(25, doc.RootElement.GetProperty("limit").GetInt32());
        var links = doc.RootElement.GetProperty("links");
        Assert.Equal(3, links.GetArrayLength());
        Assert.Equal("cccccc", links[0].GetProperty("shortCode").GetString());
        Assert.Equal("aaaaaa", links[2].GetProperty("shortCode").GetString());
    }

    [Fact]
    public async Task LinksResource_RespectsLimitAndOffset()
    {
        var owner = await SeedUserAndKeyAsync("res-pager", TestKey, ApiKeyScopes.McpRead);
        await SeedLinkAtAsync(owner, "aaaaaa", "https://example.com/a", DateTime.UtcNow.AddMinutes(-3));
        await SeedLinkAtAsync(owner, "bbbbbb", "https://example.com/b", DateTime.UtcNow.AddMinutes(-2));
        await SeedLinkAtAsync(owner, "cccccc", "https://example.com/c", DateTime.UtcNow.AddMinutes(-1));
        var client = CreateAuthorizedClient(TestKey);

        var response = await PostJsonRpcAsync(client, "resources/read", """{"uri":"shortnr://links?limit=2&offset=1"}""");
        using var json = await ReadJsonAsync(response);
        using var doc = ParseResource(json.RootElement.GetProperty("result"));
        var page = doc.RootElement;
        Assert.Equal(3, page.GetProperty("total").GetInt32());
        Assert.Equal(2, page.GetProperty("limit").GetInt32());
        Assert.Equal(1, page.GetProperty("offset").GetInt32());
        var links = page.GetProperty("links");
        Assert.Equal(2, links.GetArrayLength());
        Assert.Equal("bbbbbb", links[0].GetProperty("shortCode").GetString());
        Assert.Equal("aaaaaa", links[1].GetProperty("shortCode").GetString());
    }

    [Fact]
    public async Task LinksResource_FiltersByWorkspaceAndTag()
    {
        var owner = await SeedUserAndKeyAsync("res-filter", TestKey, ApiKeyScopes.McpRead);
        await SeedLinkAsync(owner, "aaaaaa", "https://example.com/personal");
        var workspaceId = await SeedWorkspaceAsync(owner, "gaming", "Gaming Studio");
        await AddWorkspaceMemberAsync(workspaceId, owner);
        await SeedWorkspaceLinkAsync(workspaceId, "bbbbbb", "https://example.com/ws");
        await AddTagAsync("aaaaaa", "shorts");
        var client = CreateAuthorizedClient(TestKey);

        var ws = await PostJsonRpcAsync(client, "resources/read", """{"uri":"shortnr://links?workspace=gaming"}""");
        using (var json = await ReadJsonAsync(ws))
        using (var doc = ParseResource(json.RootElement.GetProperty("result")))
        {
            var links = doc.RootElement.GetProperty("links");
            Assert.Equal(1, links.GetArrayLength());
            Assert.Equal("bbbbbb", links[0].GetProperty("shortCode").GetString());
            Assert.Equal("gaming", links[0].GetProperty("workspace").GetString());
        }

        var tagged = await PostJsonRpcAsync(client, "resources/read", """{"uri":"shortnr://links?tag=shorts"}""");
        using (var json = await ReadJsonAsync(tagged))
        using (var doc = ParseResource(json.RootElement.GetProperty("result")))
        {
            var links = doc.RootElement.GetProperty("links");
            Assert.Equal(1, links.GetArrayLength());
            Assert.Equal("aaaaaa", links[0].GetProperty("shortCode").GetString());
        }
    }

    [Fact]
    public async Task LinksResource_NoLinks_ReturnsEmptyPage()
    {
        await SeedUserAndKeyAsync("res-empty", TestKey, ApiKeyScopes.McpRead);
        var client = CreateAuthorizedClient(TestKey);

        var response = await PostJsonRpcAsync(client, "resources/read", """{"uri":"shortnr://links"}""");
        using var json = await ReadJsonAsync(response);
        using var doc = ParseResource(json.RootElement.GetProperty("result"));
        Assert.Equal(0, doc.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(0, doc.RootElement.GetProperty("links").GetArrayLength());
    }

    [Fact]
    public async Task LinkResource_ReturnsSingleLinkMetadata()
    {
        var owner = await SeedUserAndKeyAsync("res-link", TestKey, ApiKeyScopes.McpRead);
        var linkId = await SeedLinkAsync(owner, "aaaaaa", "https://example.com/a");
        await SeedMetadataAsync(linkId, utmCampaign: "fall-sale");
        await AddTagAsync("aaaaaa", "campaign");
        var client = CreateAuthorizedClient(TestKey);

        var response = await PostJsonRpcAsync(client, "resources/read", """{"uri":"shortnr://links/aaaaaa"}""");
        using var json = await ReadJsonAsync(response);
        using var doc = ParseResource(json.RootElement.GetProperty("result"));
        var link = doc.RootElement;
        Assert.Equal("aaaaaa", link.GetProperty("shortCode").GetString());
        Assert.Equal("https://example.com/a", link.GetProperty("longUrl").GetString());
        Assert.Equal("fall-sale", link.GetProperty("metadata").GetProperty("utmCampaign").GetString());
        Assert.Equal("campaign", link.GetProperty("tags")[0].GetString());
    }

    [Fact]
    public async Task LinkResource_UnknownCode_ReturnsError()
    {
        await SeedUserAndKeyAsync("res-notfound", TestKey, ApiKeyScopes.McpRead);
        var client = CreateAuthorizedClient(TestKey);

        var response = await PostJsonRpcAsync(client, "resources/read", """{"uri":"shortnr://links/nope1"}""");
        using var json = await ReadJsonAsync(response);
        using var doc = ParseResource(json.RootElement.GetProperty("result"));
        Assert.Contains("'nope1'", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task AnalyticsResource_ReturnsTimelineAndTopBreakdown()
    {
        var owner = await SeedUserAndKeyAsync("res-analytics", TestKey, ApiKeyScopes.McpRead);
        var linkId = await SeedLinkAsync(owner, "aaaaaa", "https://example.com/a");
        await SeedClickAsync(linkId, referer: "https://ref.example/a", country: "United States", device: "Desktop", browser: "Chrome");
        await SeedClickAsync(linkId, referer: "https://ref.example/a", country: "United States", device: "Desktop", browser: "Chrome");
        await SeedClickAsync(linkId, referer: "https://ref.example/b", country: "Germany", device: "Mobile", browser: "Safari");
        var client = CreateAuthorizedClient(TestKey);

        var response = await PostJsonRpcAsync(client, "resources/read", """{"uri":"shortnr://analytics/aaaaaa"}""");
        using var json = await ReadJsonAsync(response);
        using var doc = ParseResource(json.RootElement.GetProperty("result"));
        var root = doc.RootElement;
        Assert.Equal(3, root.GetProperty("total").GetInt64());
        Assert.Single(root.GetProperty("timeline").EnumerateArray());
        Assert.Equal("https://ref.example/a", root.GetProperty("referrers")[0].GetProperty("name").GetString());
        Assert.Equal(2, root.GetProperty("referrers")[0].GetProperty("count").GetInt32());
        Assert.Equal("Desktop", root.GetProperty("devices")[0].GetProperty("name").GetString());
        Assert.Equal("United States", root.GetProperty("geo")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task AnalyticsResource_InvalidDate_ReturnsError()
    {
        var owner = await SeedUserAndKeyAsync("res-baddate", TestKey, ApiKeyScopes.McpRead);
        await SeedLinkAsync(owner, "aaaaaa", "https://example.com/a");
        var client = CreateAuthorizedClient(TestKey);

        var response = await PostJsonRpcAsync(client, "resources/read", """{"uri":"shortnr://analytics/aaaaaa?from=bogus"}""");
        using var json = await ReadJsonAsync(response);
        using var doc = ParseResource(json.RootElement.GetProperty("result"));
        Assert.Contains("yyyy-MM-dd", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task WorkspacesResource_ListsMembershipsWithRoles()
    {
        var owner = await SeedUserAndKeyAsync("res-workspaces", TestKey, ApiKeyScopes.McpRead);
        var teammate = await SeedUserAsync("teammate");
        var workspaceId = await SeedWorkspaceAsync(owner, "team", "Team Co");
        await AddWorkspaceMemberAsync(workspaceId, owner, WorkspaceRole.Owner);
        await AddWorkspaceMemberAsync(workspaceId, teammate, WorkspaceRole.Editor);
        await SeedWorkspaceLinkAsync(workspaceId, "aaaaaa", "https://example.com/ws");
        var client = CreateAuthorizedClient(TestKey);

        var response = await PostJsonRpcAsync(client, "resources/read", """{"uri":"shortnr://workspaces"}""");
        using var json = await ReadJsonAsync(response);
        using var doc = ParseResource(json.RootElement.GetProperty("result"));
        var workspaces = doc.RootElement.GetProperty("workspaces");
        var workspace = workspaces.EnumerateArray().Single(w => w.GetProperty("slug").GetString() == "team");
        Assert.Equal("Team Co", workspace.GetProperty("name").GetString());
        Assert.Equal("Owner", workspace.GetProperty("role").GetString());
        Assert.Equal(2, workspace.GetProperty("memberCount").GetInt32());
        Assert.Equal(1, workspace.GetProperty("linkCount").GetInt32());
    }

    [Fact]
    public async Task Resources_RequireMcpReadScope()
    {
        await SeedUserAndKeyAsync("res-writeonly", TestKey, ApiKeyScopes.McpWrite);
        var client = CreateAuthorizedClient(TestKey);

        var response = await PostJsonRpcAsync(client, "resources/read", """{"uri":"shortnr://links"}""");
        var json = await ReadJsonAsync(response);
        using var doc = ParseResource(json.RootElement.GetProperty("result"));
        Assert.Contains(ApiKeyScopes.McpRead, doc.RootElement.GetProperty("error").GetString());
        json.Dispose();
    }

    private static JsonDocument ParseResource(JsonElement result)
    {
        var text = result.GetProperty("contents")[0].GetProperty("text").GetString();
        Assert.False(string.IsNullOrEmpty(text), "Resource text was empty.");
        return JsonDocument.Parse(text!);
    }

    private async Task SeedLinkAtAsync(long ownerUserId, string shortCode, string longUrl, DateTime createdAt)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.ShortenedUrls.Add(new ShortenedUrl
        {
            OwnerUserId = ownerUserId,
            ShortCode = shortCode,
            LongUrl = longUrl,
            DomainId = null,
            ClickCount = 0,
            CreatedAtUtc = createdAt
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedWorkspaceLinkAsync(long workspaceId, string shortCode, string longUrl)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.ShortenedUrls.Add(new ShortenedUrl
        {
            WorkspaceId = workspaceId,
            ShortCode = shortCode,
            LongUrl = longUrl,
            DomainId = null,
            ClickCount = 0,
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private async Task AddTagAsync(string shortCode, string name)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var link = await db.ShortenedUrls.SingleAsync(l => l.ShortCode == shortCode);
        db.ShortenedUrlTags.Add(new ShortenedUrlTag
        {
            ShortenedUrlId = link.Id,
            Name = name,
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }
}