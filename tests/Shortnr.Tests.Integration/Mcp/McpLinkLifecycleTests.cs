using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shortnr.Data;
using Shortnr.Data.Entities;

namespace Shortnr.Tests.Integration.Mcp;

/// <summary>
/// Exercises the PRD-024 link lifecycle via MCP: editing title/description/tags,
/// archive/unarchive (including the mcp:write scope gate), and transferring a link
/// to a workspace the caller is a member of. Read tools surface the new lifecycle
/// fields and the list status filter.
/// </summary>
public class McpLinkLifecycleTests : McpTestBase
{
    private const string TestKey = "snr_mcptest1234567890abcdef1234567890";

    [Fact]
    public async Task UpdateLink_SetsTitleDescriptionAndTags()
    {
        var owner = await SeedUserAndKeyAsync("edit-owner", TestKey, ApiKeyScopes.McpWrite);
        await SeedLinkAsync(owner, "edit01", "https://example.com/old");
        var client = CreateAuthorizedClient(TestKey);

        var response = await PostJsonRpcAsync(client, "tools/call",
            """{"name":"update_link","arguments":{"short_code":"edit01","title":"New Title","description":"A description","tags":"alpha,beta, alpha"}}""");

        using (var json = await ReadJsonAsync(response))
        {
            var text = ToolText(json.RootElement.GetProperty("result"));
            using var doc = JsonDocument.Parse(text);
            Assert.Equal("New Title", doc.RootElement.GetProperty("title").GetString());
            Assert.Equal("A description", doc.RootElement.GetProperty("description").GetString());
            var tags = doc.RootElement.GetProperty("tags");
            Assert.Equal(2, tags.GetArrayLength());
            Assert.Equal("alpha", tags[0].GetString());
            Assert.Equal("beta", tags[1].GetString());
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var link = await db.ShortenedUrls.SingleAsync(l => l.ShortCode == "edit01");
            Assert.Equal("New Title", link.Title);
            Assert.Equal("A description", link.Description);
            Assert.True(link.UpdatedAtUtc.HasValue);
        }
    }

    [Fact]
    public async Task UpdateLink_EmptyTitleAndTags_ClearsMetadata()
    {
        var owner = await SeedUserAndKeyAsync("edit-owner", TestKey, ApiKeyScopes.McpWrite);
        await SeedLinkAsync(owner, "edit02", "https://example.com/x");
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var link = await db.ShortenedUrls.SingleAsync(l => l.ShortCode == "edit02");
            link.Title = "Old Title";
            link.Description = "Old Description";
            db.ShortenedUrlTags.Add(new ShortenedUrlTag { ShortenedUrlId = link.Id, Name = "oldtag", CreatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var client = CreateAuthorizedClient(TestKey);

        var response = await PostJsonRpcAsync(client, "tools/call",
            """{"name":"update_link","arguments":{"short_code":"edit02","title":"  ","tags":""}}""");

        using (var json = await ReadJsonAsync(response))
        {
            var text = ToolText(json.RootElement.GetProperty("result"));
            using var doc = JsonDocument.Parse(text);
            Assert.True(doc.RootElement.GetProperty("title").ValueKind == JsonValueKind.Null);
            Assert.Equal(0, doc.RootElement.GetProperty("tags").GetArrayLength());
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var link = await db.ShortenedUrls.SingleAsync(l => l.ShortCode == "edit02");
            Assert.Null(link.Title);
            Assert.Equal("Old Description", link.Description);
            Assert.False(await db.ShortenedUrlTags.AnyAsync(t => t.ShortenedUrlId == link.Id));
        }
    }

    [Fact]
    public async Task ArchiveLink_Archives_AndLogsActivity()
    {
        var owner = await SeedUserAndKeyAsync("archive-owner", TestKey, ApiKeyScopes.McpWrite);
        await SeedLinkAsync(owner, "arch01", "https://example.com/a");
        var client = CreateAuthorizedClient(TestKey);

        var response = await PostJsonRpcAsync(client, "tools/call",
            """{"name":"archive_link","arguments":{"short_code":"arch01"}}""");

        using (var json = await ReadJsonAsync(response))
        {
            var text = ToolText(json.RootElement.GetProperty("result"));
            Assert.Contains("no longer redirects", text);
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var link = await db.ShortenedUrls.SingleAsync(l => l.ShortCode == "arch01");
            Assert.NotNull(link.ArchivedAtUtc);
        }

        await WaitForActivityAsync(owner, "archive_link");
    }

    [Fact]
    public async Task UnarchiveLink_Restores_AndLogsActivity()
    {
        var owner = await SeedUserAndKeyAsync("unarchive-owner", TestKey, ApiKeyScopes.McpWrite);
        var linkId = await SeedLinkAsync(owner, "arch02", "https://example.com/b");
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var link = await db.ShortenedUrls.SingleAsync(l => l.Id == linkId);
            link.ArchivedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        var client = CreateAuthorizedClient(TestKey);

        var response = await PostJsonRpcAsync(client, "tools/call",
            """{"name":"unarchive_link","arguments":{"short_code":"arch02"}}""");

        using (var json = await ReadJsonAsync(response))
        {
            var text = ToolText(json.RootElement.GetProperty("result"));
            Assert.Contains("redirects again", text);
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var link = await db.ShortenedUrls.SingleAsync(l => l.ShortCode == "arch02");
            Assert.Null(link.ArchivedAtUtc);
        }

        await WaitForActivityAsync(owner, "unarchive_link");
    }

    [Fact]
    public async Task LifecycleTools_KeyWithoutMcpWriteScope_ReturnsScopeError()
    {
        await SeedUserAndKeyAsync("read-only", TestKey, ApiKeyScopes.McpRead);
        await SeedLinkAsync(await SeedUserAsync("someone"), "arch03", "https://example.com/c");
        var client = CreateAuthorizedClient(TestKey);

        var response = await PostJsonRpcAsync(client, "tools/call",
            """{"name":"archive_link","arguments":{"short_code":"arch03"}}""");

        using var json = await ReadJsonAsync(response);
        var text = ToolText(json.RootElement.GetProperty("result"));
        Assert.Contains(ApiKeyScopes.McpWrite, text);
    }

    [Fact]
    public async Task ArchiveLink_UnknownCode_ReturnsError()
    {
        await SeedUserAndKeyAsync("archive-owner", TestKey, ApiKeyScopes.McpWrite);
        var client = CreateAuthorizedClient(TestKey);

        var response = await PostJsonRpcAsync(client, "tools/call",
            """{"name":"archive_link","arguments":{"short_code":"nope1"}}""");

        using var json = await ReadJsonAsync(response);
        var text = ToolText(json.RootElement.GetProperty("result"));
        Assert.Contains("no link", text);
    }

    [Fact]
    public async Task TransferLink_MovesPersonalLinkToWorkspace_AndLogsActivity()
    {
        var owner = await SeedUserAndKeyAsync("transfer-owner", TestKey, ApiKeyScopes.McpWrite);
        var workspaceId = await SeedWorkspaceAsync(owner, "eng", "Engineering");
        await AddWorkspaceMemberAsync(workspaceId, owner);
        await SeedLinkAsync(owner, "trns01", "https://example.com/transfer");
        var client = CreateAuthorizedClient(TestKey);

        var response = await PostJsonRpcAsync(client, "tools/call",
            """{"name":"transfer_link","arguments":{"short_code":"trns01","workspace":"eng"}}""");

        using (var json = await ReadJsonAsync(response))
        {
            var text = ToolText(json.RootElement.GetProperty("result"));
            using var doc = JsonDocument.Parse(text);
            Assert.Equal("eng", doc.RootElement.GetProperty("workspace").GetString());
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var link = await db.ShortenedUrls.SingleAsync(l => l.ShortCode == "trns01");
            Assert.Equal(workspaceId, link.WorkspaceId);
            Assert.Null(link.OwnerUserId);
        }

        await WaitForActivityAsync(owner, "transfer_link");
    }

    [Fact]
    public async Task TransferLink_NotWorkspaceMember_ReturnsError()
    {
        var owner = await SeedUserAndKeyAsync("transfer-owner", TestKey, ApiKeyScopes.McpWrite);
        await SeedWorkspaceAsync(owner, "eng", "Engineering");
        await SeedLinkAsync(owner, "trns02", "https://example.com/transfer");
        var client = CreateAuthorizedClient(TestKey);

        var response = await PostJsonRpcAsync(client, "tools/call",
            """{"name":"transfer_link","arguments":{"short_code":"trns02","workspace":"eng"}}""");

        using var json = await ReadJsonAsync(response);
        var text = ToolText(json.RootElement.GetProperty("result"));
        Assert.Contains("not a member", text);
    }

    [Fact]
    public async Task TransferLink_UnknownWorkspace_ReturnsError()
    {
        var owner = await SeedUserAndKeyAsync("transfer-owner", TestKey, ApiKeyScopes.McpWrite);
        await SeedLinkAsync(owner, "trns03", "https://example.com/transfer");
        var client = CreateAuthorizedClient(TestKey);

        var response = await PostJsonRpcAsync(client, "tools/call",
            """{"name":"transfer_link","arguments":{"short_code":"trns03","workspace":"missing"}}""");

        using var json = await ReadJsonAsync(response);
        var text = ToolText(json.RootElement.GetProperty("result"));
        Assert.Contains("not a member", text);
    }

    [Fact]
    public async Task TransferLink_InvalidSlug_ReturnsError()
    {
        var owner = await SeedUserAndKeyAsync("transfer-owner", TestKey, ApiKeyScopes.McpWrite);
        await SeedLinkAsync(owner, "trns04", "https://example.com/transfer");
        var client = CreateAuthorizedClient(TestKey);

        var response = await PostJsonRpcAsync(client, "tools/call",
            """{"name":"transfer_link","arguments":{"short_code":"trns04","workspace":"not valid!"}}""");

        using var json = await ReadJsonAsync(response);
        var text = ToolText(json.RootElement.GetProperty("result"));
        Assert.Contains("valid workspace slug", text);
    }

    [Fact]
    public async Task LifecycleTools_ResolveWorkspaceLinks_ForMembers()
    {
        var owner = await SeedUserAndKeyAsync("member-owner", TestKey, ApiKeyScopes.McpWrite);
        var other = await SeedUserAsync("member-other");
        var workspaceId = await SeedWorkspaceAsync(owner, "team", "Team");
        await AddWorkspaceMemberAsync(workspaceId, owner);
        await AddWorkspaceMemberAsync(workspaceId, other);
        await SeedLinkAsync(owner, "work01", "https://example.com/team-link");
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var link = await db.ShortenedUrls.SingleAsync(l => l.ShortCode == "work01");
            link.WorkspaceId = workspaceId;
            link.OwnerUserId = null;
            await db.SaveChangesAsync();
        }
        var client = CreateAuthorizedClient(TestKey);

        var response = await PostJsonRpcAsync(client, "tools/call",
            """{"name":"archive_link","arguments":{"short_code":"work01"}}""");

        using (var json = await ReadJsonAsync(response))
        {
            var text = ToolText(json.RootElement.GetProperty("result"));
            Assert.Contains("no longer redirects", text);
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var link = await db.ShortenedUrls.SingleAsync(l => l.ShortCode == "work01");
            Assert.NotNull(link.ArchivedAtUtc);
        }
    }

    [Fact]
    public async Task ListLinks_StatusFilter_ReturnsOnlyMatching()
    {
        var owner = await SeedUserAndKeyAsync("status-owner", TestKey, ApiKeyScopes.McpRead);
        await SeedLinkAsync(owner, "live01", "https://example.com/live");
        var archivedId = await SeedLinkAsync(owner, "gone01", "https://example.com/gone");
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var link = await db.ShortenedUrls.SingleAsync(l => l.Id == archivedId);
            link.ArchivedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        var client = CreateAuthorizedClient(TestKey);

        var archived = await PostJsonRpcAsync(client, "tools/call",
            """{"name":"list_links","arguments":{"status":"archived"}}""");
        using (var json = await ReadJsonAsync(archived))
        {
            var text = ToolText(json.RootElement.GetProperty("result"));
            using var doc = JsonDocument.Parse(text);
            Assert.Equal(1, doc.RootElement.GetArrayLength());
            Assert.Equal("gone01", doc.RootElement[0].GetProperty("shortCode").GetString());
            Assert.NotEqual(JsonValueKind.Null, doc.RootElement[0].GetProperty("archivedAtUtc").ValueKind);
        }

        var active = await PostJsonRpcAsync(client, "tools/call",
            """{"name":"list_links","arguments":{"status":"active"}}""");
        using (var json = await ReadJsonAsync(active))
        {
            var text = ToolText(json.RootElement.GetProperty("result"));
            using var doc = JsonDocument.Parse(text);
            Assert.Equal(1, doc.RootElement.GetArrayLength());
            Assert.Equal("live01", doc.RootElement[0].GetProperty("shortCode").GetString());
        }
    }

    [Fact]
    public async Task GetLinkStats_ExposesLifecycleFields()
    {
        var owner = await SeedUserAndKeyAsync("stats-owner", TestKey, ApiKeyScopes.McpRead);
        var linkId = await SeedLinkAsync(owner, "stat01", "https://example.com/stat");
        await SeedClickAsync(linkId);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var link = await db.ShortenedUrls.SingleAsync(l => l.Id == linkId);
            link.Title = "Stat Title";
            link.ArchivedAtUtc = DateTime.UtcNow;
            db.ShortenedUrlTags.Add(new ShortenedUrlTag { ShortenedUrlId = linkId, Name = "t1", CreatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var client = CreateAuthorizedClient(TestKey);

        var response = await PostJsonRpcAsync(client, "tools/call",
            """{"name":"get_link_stats","arguments":{"short_code":"stat01"}}""");

        using var json = await ReadJsonAsync(response);
        var text = ToolText(json.RootElement.GetProperty("result"));
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        Assert.Equal("Stat Title", root.GetProperty("title").GetString());
        Assert.Equal("t1", root.GetProperty("tags")[0].GetString());
        Assert.NotEqual(JsonValueKind.Null, root.GetProperty("archivedAtUtc").ValueKind);
    }
}
