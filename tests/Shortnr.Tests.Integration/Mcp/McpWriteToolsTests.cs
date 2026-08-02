using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shortnr.Data;
using Shortnr.Web.Services;

namespace Shortnr.Tests.Integration.Mcp;

/// <summary>
/// Exercises the write tools (create/update/delete links, bio-page mutations),
/// including mcp:write scope enforcement, destructive-action confirmation, and
/// the AiActivityLog audit trail.
/// </summary>
public class McpWriteToolsTests : McpTestBase
{
    private const string TestKey = "snr_mcptest1234567890abcdef1234567890";

    [Fact]
    public async Task WriteTools_KeyWithoutMcpWriteScope_ReturnsScopeError()
    {
        await SeedUserAndKeyAsync("read-only", TestKey, ApiKeyScopes.McpRead);
        var client = CreateAuthorizedClient(TestKey);

        var response = await PostJsonRpcAsync(client, "tools/call",
            """{"name":"create_short_link","arguments":{"url":"https://example.com/x"}}""");

        using var json = await ReadJsonAsync(response);
        var text = ToolText(json.RootElement.GetProperty("result"));
        Assert.Contains(ApiKeyScopes.McpWrite, text);
    }

    [Fact]
    public async Task CreateShortLink_CreatesLink_AndLogsActivity()
    {
        var owner = await SeedUserAndKeyAsync("create-owner", TestKey, ApiKeyScopes.McpWrite);
        var client = CreateAuthorizedClient(TestKey);

        var response = await PostJsonRpcAsync(client, "tools/call",
            """{"name":"create_short_link","arguments":{"url":"https://example.com/created","custom_slug":"mylink"}}""");

        using (var json = await ReadJsonAsync(response))
        {
            var text = ToolText(json.RootElement.GetProperty("result"));
            using var doc = JsonDocument.Parse(text);
            Assert.Equal("mylink", doc.RootElement.GetProperty("shortCode").GetString());
            Assert.Equal("https://example.com/created", doc.RootElement.GetProperty("longUrl").GetString());
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var link = await db.ShortenedUrls.SingleOrDefaultAsync(l => l.OwnerUserId == owner && l.ShortCode == "mylink");
            Assert.NotNull(link);
        }

        await WaitForActivityAsync(owner, "create_short_link");
    }

    [Fact]
    public async Task CreateShortLink_InvalidUrl_ReturnsError()
    {
        await SeedUserAndKeyAsync("create-owner", TestKey, ApiKeyScopes.McpWrite);
        var client = CreateAuthorizedClient(TestKey);

        var response = await PostJsonRpcAsync(client, "tools/call",
            """{"name":"create_short_link","arguments":{"url":"not-a-url"}}""");

        using var json = await ReadJsonAsync(response);
        var text = ToolText(json.RootElement.GetProperty("result"));
        Assert.Contains("http(s)", text);
    }

    [Fact]
    public async Task CreateShortLink_SlugCollision_ReturnsError()
    {
        var owner = await SeedUserAndKeyAsync("create-owner", TestKey, ApiKeyScopes.McpWrite);
        await SeedLinkAsync(owner, "taken1", "https://example.com/existing");
        var client = CreateAuthorizedClient(TestKey);

        var response = await PostJsonRpcAsync(client, "tools/call",
            """{"name":"create_short_link","arguments":{"url":"https://example.com/x","custom_slug":"taken1"}}""");

        using var json = await ReadJsonAsync(response);
        var text = ToolText(json.RootElement.GetProperty("result"));
        Assert.Contains("already in use", text);
    }

    [Fact]
    public async Task UpdateLink_DestinationWithClicks_RequiresConfirmationThenApplies()
    {
        var owner = await SeedUserAndKeyAsync("update-owner", TestKey, ApiKeyScopes.McpWrite);
        var linkId = await SeedLinkAsync(owner, "aaaaaa", "https://example.com/old", clicks: 5);
        await SeedClickAsync(linkId);
        var client = CreateAuthorizedClient(TestKey);

        var withoutConfirm = await PostJsonRpcAsync(client, "tools/call",
            """{"name":"update_link","arguments":{"short_code":"aaaaaa","url":"https://example.com/new"}}""");
        using (var json = await ReadJsonAsync(withoutConfirm))
        {
            var text = ToolText(json.RootElement.GetProperty("result"));
            Assert.Contains("confirmed=true", text);
        }

        var withConfirm = await PostJsonRpcAsync(client, "tools/call",
            """{"name":"update_link","arguments":{"short_code":"aaaaaa","url":"https://example.com/new","confirmed":true}}""");
        using (var json = await ReadJsonAsync(withConfirm))
        {
            var text = ToolText(json.RootElement.GetProperty("result"));
            using var doc = JsonDocument.Parse(text);
            Assert.Equal("https://example.com/new", doc.RootElement.GetProperty("longUrl").GetString());
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var link = await db.ShortenedUrls.SingleAsync(l => l.ShortCode == "aaaaaa");
            Assert.Equal("https://example.com/new", link.LongUrl);
        }

        await WaitForActivityAsync(owner, "update_link");
    }

    [Fact]
    public async Task UpdateLink_NoClicksOrBioPlacement_ChangesWithoutConfirmation()
    {
        var owner = await SeedUserAndKeyAsync("update-owner", TestKey, ApiKeyScopes.McpWrite);
        await SeedLinkAsync(owner, "aaaaaa", "https://example.com/old");
        var client = CreateAuthorizedClient(TestKey);

        var response = await PostJsonRpcAsync(client, "tools/call",
            """{"name":"update_link","arguments":{"short_code":"aaaaaa","url":"https://example.com/new","confirmed":false}}""");

        using (var json = await ReadJsonAsync(response))
        {
            var text = ToolText(json.RootElement.GetProperty("result"));
            using var doc = JsonDocument.Parse(text);
            Assert.Equal("https://example.com/new", doc.RootElement.GetProperty("longUrl").GetString());
        }
    }

    [Fact]
    public async Task UpdateLink_UnknownCode_ReturnsError()
    {
        await SeedUserAndKeyAsync("update-owner", TestKey, ApiKeyScopes.McpWrite);
        var client = CreateAuthorizedClient(TestKey);

        var response = await PostJsonRpcAsync(client, "tools/call",
            """{"name":"update_link","arguments":{"short_code":"nope1"}}""");

        using var json = await ReadJsonAsync(response);
        var text = ToolText(json.RootElement.GetProperty("result"));
        Assert.Contains("no link", text);
    }

    [Fact]
    public async Task DeleteLink_RequiresConfirmationThenDeletes_AndLogsActivity()
    {
        var owner = await SeedUserAndKeyAsync("delete-owner", TestKey, ApiKeyScopes.McpWrite);
        await SeedLinkAsync(owner, "aaaaaa", "https://example.com/a", clicks: 3);
        var client = CreateAuthorizedClient(TestKey);

        var withoutConfirm = await PostJsonRpcAsync(client, "tools/call",
            """{"name":"delete_link","arguments":{"short_code":"aaaaaa"}}""");
        using (var json = await ReadJsonAsync(withoutConfirm))
        {
            var text = ToolText(json.RootElement.GetProperty("result"));
            Assert.Contains("confirmed=true", text);
        }

        var withConfirm = await PostJsonRpcAsync(client, "tools/call",
            """{"name":"delete_link","arguments":{"short_code":"aaaaaa","confirmed":true}}""");
        using (var json = await ReadJsonAsync(withConfirm))
        {
            var text = ToolText(json.RootElement.GetProperty("result"));
            Assert.Contains("Deleted short link 'aaaaaa'", text);
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Null(await db.ShortenedUrls.SingleOrDefaultAsync(l => l.ShortCode == "aaaaaa"));
        }

        await WaitForActivityAsync(owner, "delete_link");
    }

    [Fact]
    public async Task AddLinkToBioPage_Appends_AndLogsActivity()
    {
        var owner = await SeedUserAndKeyAsync("bio-write", TestKey, ApiKeyScopes.McpWrite);
        var pageId = await SeedBioPageAsync(owner, "bio1", "Bio Owner");
        var linkId = await SeedLinkAsync(owner, "aaaaaa", "https://example.com/a");
        var client = CreateAuthorizedClient(TestKey);

        var response = await PostJsonRpcAsync(client, "tools/call",
            """{"name":"add_link_to_bio_page","arguments":{"short_code":"aaaaaa","title":"My Link"}}""");

        using (var json = await ReadJsonAsync(response))
        {
            var text = ToolText(json.RootElement.GetProperty("result"));
            Assert.Contains("position 1", text);
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var entry = await db.BioPageLinks.SingleAsync(b => b.BioPageId == pageId && b.ShortenedUrlId == linkId);
            Assert.Equal("My Link", entry.Title);
            Assert.Equal(0, entry.SortOrder);
        }

        await WaitForActivityAsync(owner, "add_link_to_bio_page");
    }

    [Fact]
    public async Task AddLinkToBioPage_InsertAtPosition_OrdersCorrectly()
    {
        var owner = await SeedUserAndKeyAsync("bio-write", TestKey, ApiKeyScopes.McpWrite);
        var pageId = await SeedBioPageAsync(owner, "bio1", "Bio Owner");
        var a = await SeedLinkAsync(owner, "aaaaaa", "https://example.com/a");
        var b = await SeedLinkAsync(owner, "bbbbbb", "https://example.com/b");
        await AddBioPageLinkAsync(pageId, a, "A", sortOrder: 0);
        await AddBioPageLinkAsync(pageId, b, "B", sortOrder: 1);
        var c = await SeedLinkAsync(owner, "cccccc", "https://example.com/c");
        var client = CreateAuthorizedClient(TestKey);

        var response = await PostJsonRpcAsync(client, "tools/call",
            """{"name":"add_link_to_bio_page","arguments":{"short_code":"cccccc","position":2}}""");

        using (var json = await ReadJsonAsync(response))
        {
            var text = ToolText(json.RootElement.GetProperty("result"));
            Assert.Contains("position 2", text);
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var codes = await db.BioPageLinks
                .Where(b => b.BioPageId == pageId)
                .OrderBy(b => b.SortOrder)
                .Select(b => b.ShortenedUrl!.ShortCode)
                .ToListAsync();
            Assert.Equal(new[] { "aaaaaa", "cccccc", "bbbbbb" }, codes);
        }
    }

    [Fact]
    public async Task RemoveLinkFromBioPage_RemovesAndRenumbers()
    {
        var owner = await SeedUserAndKeyAsync("bio-write", TestKey, ApiKeyScopes.McpWrite);
        var pageId = await SeedBioPageAsync(owner, "bio1", "Bio Owner");
        var a = await SeedLinkAsync(owner, "aaaaaa", "https://example.com/a");
        var b = await SeedLinkAsync(owner, "bbbbbb", "https://example.com/b");
        var c = await SeedLinkAsync(owner, "cccccc", "https://example.com/c");
        await AddBioPageLinkAsync(pageId, a, "A", sortOrder: 0);
        await AddBioPageLinkAsync(pageId, b, "B", sortOrder: 1);
        await AddBioPageLinkAsync(pageId, c, "C", sortOrder: 2);
        var client = CreateAuthorizedClient(TestKey);

        var response = await PostJsonRpcAsync(client, "tools/call",
            """{"name":"remove_link_from_bio_page","arguments":{"short_code":"bbbbbb"}}""");

        using var json = await ReadJsonAsync(response);
        var text = ToolText(json.RootElement.GetProperty("result"));
        Assert.Contains("Removed 'bbbbbb'", text);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var remaining = await db.BioPageLinks
            .Where(b => b.BioPageId == pageId)
            .OrderBy(b => b.SortOrder)
            .Select(b => b.ShortenedUrl!.ShortCode)
            .ToListAsync();
        Assert.Equal(new[] { "aaaaaa", "cccccc" }, remaining);
    }

    [Fact]
    public async Task ReorderBioPage_RearrangesLinks()
    {
        var owner = await SeedUserAndKeyAsync("bio-write", TestKey, ApiKeyScopes.McpWrite);
        var pageId = await SeedBioPageAsync(owner, "bio1", "Bio Owner");
        var a = await SeedLinkAsync(owner, "aaaaaa", "https://example.com/a");
        var b = await SeedLinkAsync(owner, "bbbbbb", "https://example.com/b");
        var c = await SeedLinkAsync(owner, "cccccc", "https://example.com/c");
        await AddBioPageLinkAsync(pageId, a, "A", sortOrder: 0);
        await AddBioPageLinkAsync(pageId, b, "B", sortOrder: 1);
        await AddBioPageLinkAsync(pageId, c, "C", sortOrder: 2);
        var client = CreateAuthorizedClient(TestKey);

        var response = await PostJsonRpcAsync(client, "tools/call",
            """{"name":"reorder_bio_page","arguments":{"order":["cccccc","aaaaaa","bbbbbb"]}}""");

        using var json = await ReadJsonAsync(response);
        var text = ToolText(json.RootElement.GetProperty("result"));
        Assert.Contains("Reordered", text);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var codes = await db.BioPageLinks
            .Where(b => b.BioPageId == pageId)
            .OrderBy(b => b.SortOrder)
            .Select(b => b.ShortenedUrl!.ShortCode)
            .ToListAsync();
        Assert.Equal(new[] { "cccccc", "aaaaaa", "bbbbbb" }, codes);
    }

    [Fact]
    public async Task ReorderBioPage_UnknownCode_ReturnsError()
    {
        var owner = await SeedUserAndKeyAsync("bio-write", TestKey, ApiKeyScopes.McpWrite);
        var pageId = await SeedBioPageAsync(owner, "bio1", "Bio Owner");
        var a = await SeedLinkAsync(owner, "aaaaaa", "https://example.com/a");
        await AddBioPageLinkAsync(pageId, a, "A", sortOrder: 0);
        var client = CreateAuthorizedClient(TestKey);

        var response = await PostJsonRpcAsync(client, "tools/call",
            """{"name":"reorder_bio_page","arguments":{"order":["zzzzzz"]}}""");

        using var json = await ReadJsonAsync(response);
        var text = ToolText(json.RootElement.GetProperty("result"));
        Assert.Contains("not on your bio page", text);
    }

    [Fact]
    public async Task SetBioPageTheme_ValidTheme_Updates()
    {
        var owner = await SeedUserAndKeyAsync("bio-write", TestKey, ApiKeyScopes.McpWrite);
        await SeedBioPageAsync(owner, "bio1", "Bio Owner", theme: "default");
        var client = CreateAuthorizedClient(TestKey);

        var response = await PostJsonRpcAsync(client, "tools/call",
            """{"name":"set_bio_page_theme","arguments":{"theme":"ocean"}}""");

        using var json = await ReadJsonAsync(response);
        var text = ToolText(json.RootElement.GetProperty("result"));
        Assert.Contains("'ocean'", text);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var page = await db.BioPages.SingleAsync(b => b.OwnerUserId == owner);
        Assert.Equal("ocean", page.Theme);
    }

    [Fact]
    public async Task SetBioPageTheme_UnknownTheme_ReturnsError()
    {
        await SeedUserAndKeyAsync("bio-write", TestKey, ApiKeyScopes.McpWrite);
        var client = CreateAuthorizedClient(TestKey);

        var response = await PostJsonRpcAsync(client, "tools/call",
            """{"name":"set_bio_page_theme","arguments":{"theme":"neon"}}""");

        using var json = await ReadJsonAsync(response);
        var text = ToolText(json.RootElement.GetProperty("result"));
        Assert.Contains("unknown theme", text);
    }
}
