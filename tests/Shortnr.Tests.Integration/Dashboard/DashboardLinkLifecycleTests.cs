using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Tests.Integration.Infrastructure;

namespace Shortnr.Tests.Integration.Dashboard;

/// <summary>
/// Verifies the PRD-024 link lifecycle UI on /dashboard: the edit form
/// (GET/POST handler=Edit), archive/unarchive HTMX actions, workspace transfer
/// (handler=Transfer), and the status filter on the search results partial.
/// </summary>
public class DashboardLinkLifecycleTests : IAsyncLifetime
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
            ShortCode = "edt111",
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

    private async Task<long> SeedWorkspaceAsync(string name, string slug, string? memberSubject = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var workspace = new Workspace { Name = name, Slug = slug, OwnerUserId = _alice.Id, CreatedAtUtc = DateTime.UtcNow };
        db.Workspaces.Add(workspace);
        await db.SaveChangesAsync();

        var subject = memberSubject ?? "alice";
        var user = await db.Users.SingleAsync(u => u.Subject == subject);
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
    // Edit form (GET handler=Edit)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetEditForm_ReturnsLinkData()
    {
        var client = AuthenticatedClient();

        var response = await client.GetAsync($"/dashboard?handler=Edit&code={_aliceLinkId}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("https://alice.com/before", html);
        Assert.Contains("edt111", html);
        Assert.Contains("Save changes", html);
    }

    [Fact]
    public async Task GetEditForm_UnknownLink_ShowsError()
    {
        var client = AuthenticatedClient();

        var response = await client.GetAsync("/dashboard?handler=Edit&code=999999");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Link not found", html);
    }

    // -------------------------------------------------------------------------
    // Edit (POST handler=Edit)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PostEdit_UpdatesLinkFields()
    {
        var client = AuthenticatedClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/dashboard?handler=Edit", token,
            ("code", _aliceLinkId.ToString()),
            ("url", "https://alice.com/after"),
            ("slug", "edt111"),
            ("title", "Q2 campaign"),
            ("description", "Summer promo"),
            ("tags", "newsletter, q2"));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Link updated", html);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var link = await db.ShortenedUrls.Include(l => l.Tags).SingleAsync(l => l.Id == _aliceLinkId);
        Assert.Equal("https://alice.com/after", link.LongUrl);
        Assert.Equal("Q2 campaign", link.Title);
        Assert.Equal("Summer promo", link.Description);
        Assert.Equal(4, link.ClickCount);
        Assert.NotNull(link.UpdatedAtUtc);
        Assert.Equal(["newsletter", "q2"], link.Tags.Select(t => t.Name).OrderBy(n => n));
    }

    [Fact]
    public async Task PostEdit_InvalidUrl_ShowsErrorAndKeepsData()
    {
        var client = AuthenticatedClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/dashboard?handler=Edit", token,
            ("code", _aliceLinkId.ToString()),
            ("url", "not-a-url"),
            ("slug", "edt111"),
            ("title", ""),
            ("description", ""),
            ("tags", ""));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Enter a valid absolute http(s) URL", html);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var link = await db.ShortenedUrls.SingleAsync(l => l.Id == _aliceLinkId);
        Assert.Equal("https://alice.com/before", link.LongUrl);
    }

    [Fact]
    public async Task PostEdit_SlugCollision_ShowsError()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ShortenedUrls.Add(new ShortenedUrl
            {
                LongUrl = "https://alice.com/other",
                ShortCode = "edt222",
                OwnerUserId = _alice.Id,
                CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var client = AuthenticatedClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/dashboard?handler=Edit", token,
            ("code", _aliceLinkId.ToString()),
            ("url", "https://alice.com/after"),
            ("slug", "edt222"),
            ("title", ""),
            ("description", ""),
            ("tags", ""));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("already exists on this domain", html);
    }

    // -------------------------------------------------------------------------
    // Archive / unarchive
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PostArchive_ArchivesLinkAndRemovesFromActiveList()
    {
        var client = AuthenticatedClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/dashboard?handler=Archive", token,
            ("code", _aliceLinkId.ToString()));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("edt111", html);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var link = await db.ShortenedUrls.SingleAsync(l => l.Id == _aliceLinkId);
        Assert.NotNull(link.ArchivedAtUtc);
    }

    [Fact]
    public async Task PostUnarchive_RestoresLinkToActiveList()
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

        var response = await PostFormAsync(client, "/dashboard?handler=Unarchive", token,
            ("code", _aliceLinkId.ToString()));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("edt111", html);

        using var scope2 = _factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var link2 = await db2.ShortenedUrls.SingleAsync(l => l.Id == _aliceLinkId);
        Assert.Null(link2.ArchivedAtUtc);
    }

    [Fact]
    public async Task PostArchive_Unauthenticated_IsRejectedBeforeHandler()
    {
        var client = _factory.CreateClientNoRedirect();
        var request = new HttpRequestMessage(HttpMethod.Post, "/dashboard?handler=Archive")
        {
            Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("code", _aliceLinkId.ToString())
            })
        };
        request.Headers.Add("HX-Request", "true");

        var response = await client.SendAsync(request);

        // Antiforgery validation runs before the page handler, so an
        // unauthenticated POST is rejected without a valid token (a 4xx) —
        // the archive action can never be reached by anonymous callers.
        Assert.True((int)response.StatusCode >= 400 && (int)response.StatusCode < 500);
    }

    // -------------------------------------------------------------------------
    // Status filter on search results
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SearchResults_DefaultFilter_ExcludesArchivedLinks()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var link = await db.ShortenedUrls.SingleAsync(l => l.Id == _aliceLinkId);
            link.ArchivedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var client = AuthenticatedClient();
        var response = await client.SendAsync(HtmxGet("/dashboard", "search-results"));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("edt111", html);
    }

    [Fact]
    public async Task SearchResults_ArchivedFilter_ShowsArchivedLinks()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var link = await db.ShortenedUrls.SingleAsync(l => l.Id == _aliceLinkId);
            link.ArchivedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var client = AuthenticatedClient();
        var response = await client.SendAsync(HtmxGet("/dashboard?status=archived", "search-results"));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("edt111", html);
        Assert.Contains("archived", html);
    }

    // -------------------------------------------------------------------------
    // Transfer (handler=Transfer)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetTransferForm_ListsMemberWorkspaces()
    {
        await SeedWorkspaceAsync("Acme", "acme");

        var client = AuthenticatedClient();
        var response = await client.GetAsync($"/dashboard?handler=Transfer&code={_aliceLinkId}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("acme", html);
        Assert.Contains("Transfer link", html);
    }

    [Fact]
    public async Task PostTransfer_MovesPersonalLinkToWorkspace()
    {
        await SeedWorkspaceAsync("Acme", "acme");
        var client = AuthenticatedClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/dashboard?handler=Transfer", token,
            ("code", _aliceLinkId.ToString()),
            ("workspace", "acme"));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Link moved to workspace", html);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var workspace = await db.Workspaces.SingleAsync(w => w.Slug == "acme");
        var link = await db.ShortenedUrls.SingleAsync(l => l.Id == _aliceLinkId);
        Assert.Equal(workspace.Id, link.WorkspaceId);
        Assert.Null(link.OwnerUserId);
        Assert.Equal(4, link.ClickCount);
    }

    [Fact]
    public async Task PostTransfer_NonMemberWorkspace_ShowsError()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var workspace = new Workspace { Name = "Rival", Slug = "rival", OwnerUserId = _alice.Id, CreatedAtUtc = DateTime.UtcNow };
            db.Workspaces.Add(workspace);
            await db.SaveChangesAsync();
        }

        var client = AuthenticatedClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/dashboard?handler=Transfer", token,
            ("code", _aliceLinkId.ToString()),
            ("workspace", "rival"));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("You can only transfer to a workspace you are a member of", html);

        using var scope2 = _factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var link = await db2.ShortenedUrls.SingleAsync(l => l.Id == _aliceLinkId);
        Assert.Null(link.WorkspaceId);
        Assert.Equal(_alice.Id, link.OwnerUserId);
    }
}
