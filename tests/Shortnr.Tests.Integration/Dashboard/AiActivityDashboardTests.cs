using System.Net;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Tests.Integration.Infrastructure;

namespace Shortnr.Tests.Integration.Dashboard;

/// <summary>
/// Verifies the AI activity dashboard (/dashboard/activity): authentication
/// enforcement, owner-scoped listing, and HTMX partial responses.
/// </summary>
public class AiActivityDashboardTests : IAsyncLifetime
{
    private readonly ShortnrWebAppFactory _authEnabled = new(authEnabled: true);
    private readonly ShortnrWebAppFactory _authDisabled = new(authEnabled: false);

    private User _alice = null!;
    private User _bob = null!;
    private User _carol = null!;

    public async Task InitializeAsync()
    {
        using var scope = _authEnabled.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();

        _alice = new User { Issuer = ShortnrWebAppFactory.TestIssuer, Subject = "alice", Email = "alice@example.com", CreatedAtUtc = DateTime.UtcNow, LastLoginAtUtc = DateTime.UtcNow };
        _bob = new User { Issuer = ShortnrWebAppFactory.TestIssuer, Subject = "bob", Email = "bob@example.com", CreatedAtUtc = DateTime.UtcNow, LastLoginAtUtc = DateTime.UtcNow };
        _carol = new User { Issuer = ShortnrWebAppFactory.TestIssuer, Subject = "carol", Email = "carol@example.com", CreatedAtUtc = DateTime.UtcNow, LastLoginAtUtc = DateTime.UtcNow };
        db.Users.AddRange(_alice, _bob, _carol);
        await db.SaveChangesAsync();

        var key = new ApiKey
        {
            OwnerUserId = _alice.Id,
            KeyHash = "hash",
            KeyPrefix = "snr_",
            Label = "alice agent",
            CreatedAtUtc = DateTime.UtcNow
        };
        db.ApiKeys.Add(key);
        await db.SaveChangesAsync();

        db.AiActivityLogs.AddRange(
            new AiActivityLog { OwnerUserId = _alice.Id, ApiKeyId = key.Id, Action = "create_short_link", TargetEntityType = "ShortenedUrl", Summary = "Alice created link 'aaa111'", CreatedAtUtc = DateTime.UtcNow },
            new AiActivityLog { OwnerUserId = _alice.Id, Action = "delete_link", TargetEntityType = "ShortenedUrl", Summary = "Alice deleted link 'aaa222'", CreatedAtUtc = DateTime.UtcNow },
            new AiActivityLog { OwnerUserId = _bob.Id, Action = "create_short_link", TargetEntityType = "ShortenedUrl", Summary = "Bob created link 'bbb111'", CreatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();

        using var scope2 = _authDisabled.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        await db2.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _authEnabled.DisposeAsync();
        await _authDisabled.DisposeAsync();
    }

    private HttpClient AuthenticatedClient()
    {
        var authState = _authEnabled.Services.GetRequiredService<TestAuthState>();
        authState.SetAuthenticatedUser("alice", ShortnrWebAppFactory.TestIssuer);
        return _authEnabled.CreateClientNoRedirect();
    }

    private static HttpRequestMessage HtmxGet(string url, string target) =>
        new(HttpMethod.Get, url)
        {
            Headers = { { "HX-Request", "true" }, { "HX-Target", target } }
        };

    // -------------------------------------------------------------------------
    // Auth enforcement
    // -------------------------------------------------------------------------

    [Fact]
    public async Task WhenAuthEnabled_Unauthenticated_FullPageRequest_RedirectsToIndex()
    {
        var client = _authEnabled.CreateClientNoRedirect();

        var response = await client.GetAsync("/dashboard/activity");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task WhenAuthEnabled_Unauthenticated_HtmxRequest_Returns401()
    {
        var client = _authEnabled.CreateClientNoRedirect();

        var response = await client.SendAsync(HtmxGet("/dashboard/activity", "ai-activity"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WhenAuthDisabled_Unauthenticated_Returns200()
    {
        var client = _authDisabled.CreateClientNoRedirect();

        var response = await client.GetAsync("/dashboard/activity");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Owner scoping
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FullPage_AsAlice_ShowsOnlyAliceActivity()
    {
        var client = AuthenticatedClient();

        var response = await client.GetAsync("/dashboard/activity");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Alice created link", html);
        Assert.Contains("Alice deleted link", html);
        Assert.DoesNotContain("Bob created link", html);
    }

    [Fact]
    public async Task FullPage_ShowsApiKeyLabel()
    {
        var client = AuthenticatedClient();

        var response = await client.GetAsync("/dashboard/activity");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("alice agent", html);
    }

    [Fact]
    public async Task HtmxPartial_AsAlice_ShowsOnlyAliceActivity()
    {
        var client = AuthenticatedClient();

        var response = await client.SendAsync(HtmxGet("/dashboard/activity", "ai-activity"));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("create_short_link", html);
        Assert.Contains("Alice created link", html);
        Assert.DoesNotContain("Bob created link", html);
    }

    [Fact]
    public async Task EmptyState_WhenNoActivity_ShowsPlaceholder()
    {
        var authState = _authEnabled.Services.GetRequiredService<TestAuthState>();
        authState.SetAuthenticatedUser("carol", ShortnrWebAppFactory.TestIssuer);
        var client = _authEnabled.CreateClientNoRedirect();
        var response = await client.GetAsync("/dashboard/activity");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("No AI activity yet", html);
    }
}
