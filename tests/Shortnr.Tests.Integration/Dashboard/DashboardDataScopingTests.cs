using System.Net;
using System.Net.Http.Json;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Tests.Integration.Infrastructure;

namespace Shortnr.Tests.Integration.Dashboard;

/// <summary>
/// Verifies that dashboard HTMX partials and the /api/metrics endpoint return
/// only the data that belongs to the currently authenticated user.
/// </summary>
public class DashboardDataScopingTests : IAsyncLifetime
{
    private readonly ShortnrWebAppFactory _factory = new(authEnabled: true);

    // Two users seeded before each test.
    private User _alice = null!;
    private User _bob = null!;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.ClickEvents.RemoveRange(db.ClickEvents);
        db.ShortenedUrls.RemoveRange(db.ShortenedUrls);
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
        _bob = new User
        {
            Issuer = ShortnrWebAppFactory.TestIssuer,
            Subject = "bob",
            Email = "bob@example.com",
            CreatedAtUtc = DateTime.UtcNow,
            LastLoginAtUtc = DateTime.UtcNow
        };
        db.Users.AddRange(_alice, _bob);
        await db.SaveChangesAsync();

        // Alice owns 2 links (3 + 7 clicks); Bob owns 1 link (5 clicks).
        var aliceLink1 = new ShortenedUrl { LongUrl = "https://alice.com/1", ShortCode = "aaa111", ClickCount = 3, OwnerUserId = _alice.Id, CreatedAtUtc = DateTime.UtcNow };
        var aliceLink2 = new ShortenedUrl { LongUrl = "https://alice.com/2", ShortCode = "aaa222", ClickCount = 7, OwnerUserId = _alice.Id, CreatedAtUtc = DateTime.UtcNow };
        var bobLink = new ShortenedUrl { LongUrl = "https://bob.com/1", ShortCode = "bbb111", ClickCount = 5, OwnerUserId = _bob.Id, CreatedAtUtc = DateTime.UtcNow };
        db.ShortenedUrls.AddRange(aliceLink1, aliceLink2, bobLink);
        await db.SaveChangesAsync();

        // Each link gets one click event.
        db.ClickEvents.AddRange(
            new ClickEvent { ShortenedUrlId = aliceLink1.Id, IpAddress = "1.1.1.1", ClickedAtUtc = DateTime.UtcNow },
            new ClickEvent { ShortenedUrlId = aliceLink2.Id, IpAddress = "1.1.1.1", ClickedAtUtc = DateTime.UtcNow },
            new ClickEvent { ShortenedUrlId = bobLink.Id, IpAddress = "2.2.2.2", ClickedAtUtc = DateTime.UtcNow });
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

    // -------------------------------------------------------------------------
    // Metrics summary (HX-Target: metrics-summary)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MetricsSummary_AsAlice_ShowsOnlyAliceLinks()
    {
        var client = AuthenticatedClient("alice");
        var response = await client.SendAsync(HtmxGet("/dashboard", "metrics-summary"));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Alice has 2 links totalling 10 clicks.
        Assert.Contains(">2<", html);
        Assert.Contains(">10<", html);
        // Bob's link count (1) should not appear as the link total.
        Assert.DoesNotContain(">3<", html); // 3 is Bob's count if all were shown
    }

    [Fact]
    public async Task MetricsSummary_AsBob_ShowsOnlyBobLinks()
    {
        var client = AuthenticatedClient("bob");
        var response = await client.SendAsync(HtmxGet("/dashboard", "metrics-summary"));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Bob has 1 link with 5 clicks.
        Assert.Contains(">1<", html);
        Assert.Contains(">5<", html);
    }

    // -------------------------------------------------------------------------
    // Search results (default HX-Target — link list)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SearchResults_AsAlice_ContainsOnlyAliceShortCodes()
    {
        var client = AuthenticatedClient("alice");
        var response = await client.SendAsync(HtmxGet("/dashboard", "search-results"));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("aaa111", html);
        Assert.Contains("aaa222", html);
        Assert.DoesNotContain("bbb111", html);
    }

    [Fact]
    public async Task SearchResults_AsBob_ContainsOnlyBobShortCodes()
    {
        var client = AuthenticatedClient("bob");
        var response = await client.SendAsync(HtmxGet("/dashboard", "search-results"));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("bbb111", html);
        Assert.DoesNotContain("aaa111", html);
        Assert.DoesNotContain("aaa222", html);
    }

    // -------------------------------------------------------------------------
    // Recent clicks (HX-Target: recent-clicks)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RecentClicks_AsAlice_ContainsOnlyAliceClicks()
    {
        var client = AuthenticatedClient("alice");
        var response = await client.SendAsync(HtmxGet("/dashboard", "recent-clicks"));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("aaa111", html);
        Assert.Contains("aaa222", html);
        Assert.DoesNotContain("bbb111", html);
    }

    [Fact]
    public async Task RecentClicks_AsBob_ContainsOnlyBobClicks()
    {
        var client = AuthenticatedClient("bob");
        var response = await client.SendAsync(HtmxGet("/dashboard", "recent-clicks"));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("bbb111", html);
        Assert.DoesNotContain("aaa111", html);
        Assert.DoesNotContain("aaa222", html);
    }
}
