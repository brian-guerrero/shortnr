using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Tests.Integration.Infrastructure;

namespace Shortnr.Tests.Integration.Dashboard;

/// <summary>
/// Verifies the PRD-019 bulk link actions on /dashboard: bulk delete with undo,
/// bulk archive/unarchive, bulk move to a workspace, bulk tag/untag, the
/// per-row link-detail drill-down modal, and the Cmd+K command palette.
/// </summary>
public class DashboardBulkActionTests : IAsyncLifetime
{
    private static readonly Regex AntiforgeryTokenRegex = new(
        @"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""",
        RegexOptions.Compiled);

    private readonly ShortnrWebAppFactory _factory = new(authEnabled: true);

    private User _alice = null!;
    private long _aliceLinkId;
    private string _aliceLinkCode = null!;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.ClickEvents.RemoveRange(db.ClickEvents);
        db.ShortenedUrls.RemoveRange(db.ShortenedUrls);
        db.WorkspaceMembers.RemoveRange(db.WorkspaceMembers);
        db.Workspaces.RemoveRange(db.Workspaces);
        db.Users.RemoveRange(db.Users);
        await db.SaveChangesAsync();

        _alice = new User
        {
            Issuer = ShortnrWebAppFactory.TestIssuer,
            Subject = "alice",
            Email = "alice@example.com",
            CreatedAtUtc = DateTime.UtcNow,
            LastLoginAtUtc = DateTime.UtcNow
        };
        db.Users.Add(_alice);
        await db.SaveChangesAsync();

        var aliceLink = new ShortenedUrl
        {
            LongUrl = "https://alice.com/before",
            ShortCode = "blk111",
            ClickCount = 4,
            OwnerUserId = _alice.Id,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.ShortenedUrls.Add(aliceLink);
        await db.SaveChangesAsync();

        _aliceLinkId = aliceLink.Id;
        _aliceLinkCode = aliceLink.ShortCode;
    }

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    private HttpClient AuthenticatedClient()
    {
        var authState = _factory.Services.GetRequiredService<TestAuthState>();
        authState.SetAuthenticatedUser("alice", ShortnrWebAppFactory.TestIssuer);
        return _factory.CreateClientNoRedirect();
    }

    private async Task<long> SeedWorkspaceAsync(string name, string slug)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var workspace = new Workspace { Name = name, Slug = slug, OwnerUserId = _alice.Id, CreatedAtUtc = DateTime.UtcNow };
        db.Workspaces.Add(workspace);
        await db.SaveChangesAsync();

        var user = await db.Users.SingleAsync(u => u.Subject == "alice");
        db.WorkspaceMembers.Add(new WorkspaceMember
        {
            WorkspaceId = workspace.Id,
            UserId = user.Id,
            Role = WorkspaceRole.Owner,
            InvitedAtUtc = DateTime.UtcNow,
            JoinedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return workspace.Id;
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        var response = await client.SendAsync(HtmxGet("/dashboard?status=all", "search-results"));
        var html = await response.Content.ReadAsStringAsync();
        var match = AntiforgeryTokenRegex.Match(html);
        Assert.True(match.Success, "Antiforgery token not found in search-results partial.");
        return match.Groups[1].Value;
    }

    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string path, string token, params (string Name, string Value)[] fields)
    {
        var pairs = fields
            .Select(f => new KeyValuePair<string, string>(f.Name, f.Value))
            .ToList();
        pairs.Add(new KeyValuePair<string, string>("__RequestVerificationToken", token));
        return await client.PostAsync(path, new FormUrlEncodedContent(pairs));
    }

    private static HttpRequestMessage HtmxGet(string url, string target) =>
        new(HttpMethod.Get, url)
        {
            Headers = { { "HX-Request", "true" }, { "HX-Target", target } }
        };

    // -------------------------------------------------------------------------
    // Search-results partial: bulk toolbar + row checkboxes
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SearchResults_RendersBulkToolbar()
    {
        var client = AuthenticatedClient();
        var response = await client.SendAsync(HtmxGet("/dashboard", "search-results"));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-bulk-toolbar", html);
        Assert.Contains("data-bulk-count", html);
        Assert.Contains("data-select-row", html);
        Assert.Contains("data-select-all", html);
        // Per-row Stats/Edit/Transfer controls remain alongside the bulk toolbar.
        Assert.Contains("handler=Detail", html);
        Assert.Contains("handler=Edit", html);
        Assert.Contains("handler=Transfer", html);
    }

    // -------------------------------------------------------------------------
    // Bulk delete + undo
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PostBulkDelete_RemovesLinksAndReturnsUndoableToast()
    {
        var client = AuthenticatedClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/dashboard?handler=BulkDelete", token,
            ("ids", _aliceLinkId.ToString()));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Deleted 1 link", html);
        // The undo affordance is on the toast, out-of-band.
        Assert.Contains("hx-swap-oob", html);
        Assert.Contains("handler=Undo", html);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.ShortenedUrls.AnyAsync(l => l.Id == _aliceLinkId));
    }

    [Fact]
    public async Task PostBulkDelete_WithNoSelection_ShowsNeutralMessage()
    {
        var client = AuthenticatedClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/dashboard?handler=BulkDelete", token);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("No links selected", html);
    }

    [Fact]
    public async Task PostBulkDelete_Undo_RestoresLinkWithMetadataAndTags()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ShortenedUrlTags.Add(new ShortenedUrlTag
            {
                ShortenedUrlId = _aliceLinkId,
                Name = "newsletter",
                CreatedAtUtc = DateTime.UtcNow
            });
            db.ShortenedUrlMetadatas.Add(new ShortenedUrlMetadata
            {
                ShortenedUrlId = _aliceLinkId,
                UtmSource = "newsletter",
                PixelId = "1234567890"
            });
            await db.SaveChangesAsync();
        }

        var client = AuthenticatedClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var deleteResponse = await PostFormAsync(client, "/dashboard?handler=BulkDelete", token,
            ("ids", _aliceLinkId.ToString()));
        var deleteHtml = await deleteResponse.Content.ReadAsStringAsync();
        var undoToken = AntiforgeryTokenRegex.Match(deleteHtml).Success
            ? ExtractUndoToken(deleteHtml)
            : null;
        Assert.NotNull(undoToken);

        var undoResponse = await PostFormAsync(client, "/dashboard?handler=Undo", token,
            ("token", undoToken!));
        var undoHtml = await undoResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, undoResponse.StatusCode);
        Assert.Contains("Restored 1 link", undoHtml);

        using var scope2 = _factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var link = await db2.ShortenedUrls
            .Include(l => l.Tags)
            .Include(l => l.Metadata)
            .SingleAsync(l => l.Id == _aliceLinkId);
        Assert.Equal("https://alice.com/before", link.LongUrl);
        Assert.Equal(4, link.ClickCount);
        Assert.Equal("newsletter", Assert.Single(link.Tags).Name);
        Assert.Equal("newsletter", link.Metadata!.UtmSource);
        Assert.Equal("1234567890", link.Metadata.PixelId);
    }

    [Fact]
    public async Task PostBulkDelete_Undo_WithUnknownToken_ShowsErrorToast()
    {
        var client = AuthenticatedClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/dashboard?handler=Undo", token,
            ("token", "no-such-token"));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no longer available", html);
    }

    // -------------------------------------------------------------------------
    // Bulk archive / unarchive
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PostBulkArchive_ArchivesSelectedLinks()
    {
        var client = AuthenticatedClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/dashboard?handler=BulkArchive", token,
            ("ids", _aliceLinkId.ToString()));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Archived 1 link", html);
        // The active default filter excludes it, so the refreshed table is empty.
        Assert.DoesNotContain(_aliceLinkCode, html);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var link = await db.ShortenedUrls.SingleAsync(l => l.Id == _aliceLinkId);
        Assert.NotNull(link.ArchivedAtUtc);
    }

    [Fact]
    public async Task PostBulkUnarchive_RestoresArchivedLinks()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var link = await db.ShortenedUrls.SingleAsync(l => l.Id == _aliceLinkId);
            link.ArchivedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var client = AuthenticatedClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/dashboard?handler=BulkUnarchive", token,
            ("ids", _aliceLinkId.ToString()));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Restored 1 link", html);

        using var scope2 = _factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var link2 = await db2.ShortenedUrls.SingleAsync(l => l.Id == _aliceLinkId);
        Assert.Null(link2.ArchivedAtUtc);
    }

    // -------------------------------------------------------------------------
    // Bulk move to workspace
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PostBulkMove_MovesLinksToWorkspace()
    {
        await SeedWorkspaceAsync("Acme", "acme");
        var client = AuthenticatedClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/dashboard?handler=BulkMove", token,
            ("ids", _aliceLinkId.ToString()),
            ("workspace", "acme"));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Moved 1 link", html);
        Assert.Contains("Acme", html);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var workspace = await db.Workspaces.SingleAsync(w => w.Slug == "acme");
        var link = await db.ShortenedUrls.SingleAsync(l => l.Id == _aliceLinkId);
        Assert.Equal(workspace.Id, link.WorkspaceId);
        Assert.Null(link.OwnerUserId);
    }

    [Fact]
    public async Task PostBulkMove_NonMemberWorkspace_ShowsError()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Workspaces.Add(new Workspace { Name = "Rival", Slug = "rival", OwnerUserId = _alice.Id, CreatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var client = AuthenticatedClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/dashboard?handler=BulkMove", token,
            ("ids", _aliceLinkId.ToString()),
            ("workspace", "rival"));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("only move links to a workspace you are a member of", html);

        using var scope2 = _factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var link = await db2.ShortenedUrls.SingleAsync(l => l.Id == _aliceLinkId);
        Assert.Null(link.WorkspaceId);
    }

    // -------------------------------------------------------------------------
    // Bulk tag / untag
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PostBulkTag_AddsTagToSelectedLinks()
    {
        var client = AuthenticatedClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/dashboard?handler=BulkTag", token,
            ("ids", _aliceLinkId.ToString()),
            ("tags", "newsletter, q2"));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Tagged 1 link", html);
        Assert.Contains("newsletter", html);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var link = await db.ShortenedUrls.Include(l => l.Tags).SingleAsync(l => l.Id == _aliceLinkId);
        Assert.Equal(["newsletter", "q2"], link.Tags.Select(t => t.Name).OrderBy(n => n));
    }

    [Fact]
    public async Task PostBulkTag_NoTagsEntered_ShowsMessage()
    {
        var client = AuthenticatedClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/dashboard?handler=BulkTag", token,
            ("ids", _aliceLinkId.ToString()));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Enter at least one tag", html);
    }

    [Fact]
    public async Task PostBulkUntag_RemovesTagFromSelectedLinks()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ShortenedUrlTags.Add(new ShortenedUrlTag
            {
                ShortenedUrlId = _aliceLinkId,
                Name = "newsletter",
                CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var client = AuthenticatedClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/dashboard?handler=BulkUntag", token,
            ("ids", _aliceLinkId.ToString()),
            ("tags", "newsletter"));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Removed tag(s) from 1 link", html);

        using var scope2 = _factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var link = await db2.ShortenedUrls.Include(l => l.Tags).SingleAsync(l => l.Id == _aliceLinkId);
        Assert.Empty(link.Tags);
    }

    // -------------------------------------------------------------------------
    // Link detail modal (GET handler=Detail)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetDetail_ReturnsTimelineBreakdowns()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ClickEvents.Add(new ClickEvent
            {
                ShortenedUrlId = _aliceLinkId,
                ClickedAtUtc = DateTime.UtcNow.AddDays(-1),
                UserAgent = "iPhone",
                Referer = "https://news.example.com/item"
            });
            await db.SaveChangesAsync();
        }

        var client = AuthenticatedClient();
        var response = await client.GetAsync($"/dashboard?handler=Detail&code={_aliceLinkId}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Clicks (30 days)", html);
        Assert.Contains("Top referrers", html);
        Assert.Contains("Geography", html);
        Assert.Contains("news.example.com", html);
        // Chart JSON rides along for the canvases without a second round trip.
        Assert.Contains("link-detail-chart-data", html);
        Assert.Contains("timeline", html);
    }

    [Fact]
    public async Task GetDetail_UnknownLink_ReturnsNotFound()
    {
        var client = AuthenticatedClient();
        var response = await client.GetAsync("/dashboard?handler=Detail&code=999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetDetail_WithNoClicks_ShowsEmptyStates()
    {
        var client = AuthenticatedClient();
        var response = await client.GetAsync($"/dashboard?handler=Detail&code={_aliceLinkId}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("No referrers yet", html);
        Assert.Contains("No geo data yet", html);
    }

    // -------------------------------------------------------------------------
    // Command palette (Cmd+K)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Layout_IncludesCommandPaletteForAuthenticatedUser()
    {
        var client = AuthenticatedClient();
        var response = await client.GetAsync("/dashboard");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-palette", html);
        Assert.Contains("data-palette-opener", html);
        Assert.Contains("Create short link", html);
        Assert.Contains("Toggle color theme", html);
    }

    [Fact]
    public async Task Layout_Palette_IncludesMemberWorkspaceSwitchOptions()
    {
        await SeedWorkspaceAsync("Acme", "acme");
        var client = AuthenticatedClient();
        var response = await client.GetAsync("/dashboard");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Switch to workspace Acme", html);
        Assert.Contains("Personal", html);
        Assert.Contains("value=\"acme\"", html);
    }

    [Fact]
    public async Task Layout_Palette_IncludesThemeGroupWithActiveMarker()
    {
        var client = AuthenticatedClient();
        var response = await client.GetAsync("/dashboard");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // The root panel's entry point into the themes sub-panel...
        Assert.Contains("data-palette-open-panel=\"themes\"", html);
        Assert.Contains("Switch theme", html);
        // ...which is rendered (hidden until opened) with every preset listed...
        Assert.Contains("data-palette-panel=\"themes\"", html);
        Assert.Contains("action=\"/theme/switch\"", html);
        Assert.Contains("Switch to Midnight theme", html);
        Assert.Contains("value=\"midnight\"", html);
        // ...and with no stored preference, Default is the active one.
        Assert.Contains("Switch to Default theme", html);
        Assert.Contains("value=\"default\"", html);
    }

    private static string? ExtractUndoToken(string html)
    {
        var match = Regex.Match(html, @"name=""token"" value=""([^""]+)""");
        return match.Success ? match.Groups[1].Value : null;
    }
}
