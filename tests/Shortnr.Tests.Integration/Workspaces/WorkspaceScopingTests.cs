using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Tests.Integration.Infrastructure;

namespace Shortnr.Tests.Integration.Workspaces;

/// <summary>
/// Verifies that an active workspace (the <c>snr_workspace</c> cookie) scope-limits
/// every data surface: dashboard partials, /api/metrics and domain listing. The
/// scoping is <b>exclusive</b> — with a workspace active, the user's personal links
/// are hidden entirely. AI activity stays personal-only (no workspace FK).
/// </summary>
public class WorkspaceScopingTests : IAsyncLifetime
{
    private readonly ShortnrWebAppFactory _factory = new(authEnabled: true);

    private User _alice = null!;
    private User _bob = null!;
    private Workspace _workspace = null!;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.AiActivityLogs.RemoveRange(db.AiActivityLogs);
        db.ClickEvents.RemoveRange(db.ClickEvents);
        db.ShortenedUrls.RemoveRange(db.ShortenedUrls);
        db.Domains.RemoveRange(db.Domains);
        db.WorkspaceMembers.RemoveRange(db.WorkspaceMembers);
        db.Workspaces.RemoveRange(db.Workspaces);
        db.Users.RemoveRange(db.Users);
        await db.SaveChangesAsync();

        _alice = new User { Issuer = ShortnrWebAppFactory.TestIssuer, Subject = "alice", Email = "alice@example.com", CreatedAtUtc = DateTime.UtcNow, LastLoginAtUtc = DateTime.UtcNow };
        _bob = new User { Issuer = ShortnrWebAppFactory.TestIssuer, Subject = "bob", Email = "bob@example.com", CreatedAtUtc = DateTime.UtcNow, LastLoginAtUtc = DateTime.UtcNow };
        db.Users.AddRange(_alice, _bob);
        await db.SaveChangesAsync();

        _workspace = new Workspace { Name = "Acme", Slug = "acme", OwnerUserId = _alice.Id, CreatedAtUtc = DateTime.UtcNow };
        db.Workspaces.Add(_workspace);
        await db.SaveChangesAsync();

        db.WorkspaceMembers.AddRange(
            new WorkspaceMember { WorkspaceId = _workspace.Id, UserId = _alice.Id, Role = WorkspaceRole.Owner, InvitedAtUtc = DateTime.UtcNow, JoinedAtUtc = DateTime.UtcNow },
            new WorkspaceMember { WorkspaceId = _workspace.Id, UserId = _bob.Id, Role = WorkspaceRole.Editor, InvitedAtUtc = DateTime.UtcNow, JoinedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();

        // Alice's personal links (no workspace)...
        db.ShortenedUrls.AddRange(
            new ShortenedUrl { LongUrl = "https://alice.com/personal", ShortCode = "pers001", ClickCount = 99, OwnerUserId = _alice.Id, CreatedAtUtc = DateTime.UtcNow },
            // ...and the workspace links (Alice's + Bob's). Workspace links carry
            // no OwnerUserId (see IndexModel.CreateAsync) so they stay out of the
            // personal/owner-scoped views entirely.
            new ShortenedUrl { LongUrl = "https://acme.com/one", ShortCode = "wsacm01", ClickCount = 4, OwnerUserId = null, WorkspaceId = _workspace.Id, CreatedAtUtc = DateTime.UtcNow },
            new ShortenedUrl { LongUrl = "https://acme.com/two", ShortCode = "wsbob01", ClickCount = 6, OwnerUserId = null, WorkspaceId = _workspace.Id, CreatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();

        // A personal domain and a workspace-scoped domain for Alice.
        db.Domains.AddRange(
            new Domain { Hostname = "personal.example.com", OwnerUserId = _alice.Id, IsVerified = true, VerificationToken = "p", CreatedAtUtc = DateTime.UtcNow },
            new Domain { Hostname = "ws.example.com", WorkspaceId = _workspace.Id, IsVerified = true, VerificationToken = "w", CreatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();

        // AI activity has no workspace FK: it must stay visible under Alice's
        // owner identity regardless of the active workspace. (No apostrophes in
        // the summaries — Razor HTML-encodes them as &#x27;.)
        db.AiActivityLogs.AddRange(
            new AiActivityLog { OwnerUserId = _alice.Id, Action = "create_short_link", TargetEntityType = "ShortenedUrl", Summary = "Alice personal AI activity", CreatedAtUtc = DateTime.UtcNow },
            new AiActivityLog { OwnerUserId = _bob.Id, Action = "delete_link", TargetEntityType = "ShortenedUrl", Summary = "Bob AI activity", CreatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private HttpClient AuthenticatedClient(string subject)
    {
        var authState = _factory.Services.GetRequiredService<TestAuthState>();
        authState.SetAuthenticatedUser(subject, ShortnrWebAppFactory.TestIssuer);
        return _factory.CreateClientNoRedirect();
    }

    private static HttpRequestMessage HtmxGet(string url, string target) =>
        new(HttpMethod.Get, url)
        {
            Headers = { { "HX-Request", "true" }, { "HX-Target", target } }
        };

    private static HttpRequestMessage WithWorkspaceCookie(HttpRequestMessage request, string slug)
    {
        request.Headers.Add("Cookie", $"snr_workspace={slug}");
        return request;
    }

    // -------------------------------------------------------------------------
    // Dashboard link list
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DashboardSearch_WithActiveWorkspace_ShowsOnlyWorkspaceLinks()
    {
        var client = AuthenticatedClient("alice");

        var response = await client.SendAsync(WithWorkspaceCookie(
            HtmxGet("/dashboard", "search-results"), "acme"));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Workspace links include the other member's (Bob's) links...
        Assert.Contains("wsacm01", html);
        Assert.Contains("wsbob01", html);
        // ...but scoping is EXCLUSIVE: personal links are hidden entirely.
        Assert.DoesNotContain("pers001", html);
    }

    [Fact]
    public async Task DashboardSearch_WithoutActiveWorkspace_ShowsOnlyPersonalLinks()
    {
        var client = AuthenticatedClient("alice");

        var response = await client.SendAsync(HtmxGet("/dashboard", "search-results"));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("pers001", html);
        Assert.DoesNotContain("wsacm01", html);
        Assert.DoesNotContain("wsbob01", html);
    }

    [Fact]
    public async Task MetricsSummary_WithActiveWorkspace_CountsOnlyWorkspaceLinks()
    {
        var client = AuthenticatedClient("alice");

        var response = await client.SendAsync(WithWorkspaceCookie(
            HtmxGet("/dashboard", "metrics-summary"), "acme"));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // 2 workspace links totalling 10 clicks; the personal link (99 clicks) is excluded.
        Assert.Contains(">2<", html);
        Assert.Contains(">10<", html);
        Assert.DoesNotContain(">99<", html);
    }

    // -------------------------------------------------------------------------
    // /api/metrics
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ApiMetrics_WithActiveWorkspace_ReturnsOnlyWorkspaceLinks()
    {
        var client = AuthenticatedClient("alice");

        var response = await client.SendAsync(WithWorkspaceCookie(
            new HttpRequestMessage(HttpMethod.Get, "/api/metrics"), "acme"));
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, json.GetProperty("totalLinks").GetInt32());
        Assert.Equal(10, json.GetProperty("totalClicks").GetInt64());
        var codes = json.GetProperty("topLinks").EnumerateArray()
            .Select(l => l.GetProperty("shortCode").GetString())
            .ToList();
        Assert.All(codes, code => Assert.StartsWith("ws", code!));
        Assert.DoesNotContain("pers001", codes);
    }

    [Fact]
    public async Task ApiMetrics_WithoutActiveWorkspace_ReturnsOnlyPersonalLinks()
    {
        var client = AuthenticatedClient("alice");

        var response = await client.GetAsync("/api/metrics");
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(1, json.GetProperty("totalLinks").GetInt32());
        Assert.Equal(99, json.GetProperty("totalClicks").GetInt64());
    }

    // -------------------------------------------------------------------------
    // Domain listing
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DomainListing_WithActiveWorkspace_ShowsOnlyWorkspaceDomains()
    {
        var client = AuthenticatedClient("alice");

        var response = await client.SendAsync(WithWorkspaceCookie(
            new HttpRequestMessage(HttpMethod.Get, "/settings/domains"), "acme"));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("ws.example.com", html);
        Assert.DoesNotContain("personal.example.com", html);
    }

    // -------------------------------------------------------------------------
    // AI activity — personal-only
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AiActivity_WithActiveWorkspace_StaysPersonalOnly()
    {
        var client = AuthenticatedClient("alice");

        var response = await client.SendAsync(WithWorkspaceCookie(
            new HttpRequestMessage(HttpMethod.Get, "/dashboard/activity"), "acme"));
        var html = await response.Content.ReadAsStringAsync();

        // Activity is not workspace-scoped: Alice's personal activity stays visible
        // even with a workspace active, and Bob's is still hidden (owner scoping).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Alice personal AI activity", html);
        Assert.DoesNotContain("Bob AI activity", html);
    }
}
