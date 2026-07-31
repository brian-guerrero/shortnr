using System.Net;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Tests.Integration.Infrastructure;

namespace Shortnr.Tests.Integration.Dashboard;

/// <summary>
/// Verifies the dashboard link table's domain column and the domain filter.
/// </summary>
public class DashboardDomainFilterTests : IAsyncLifetime
{
    private readonly ShortnrWebAppFactory _factory = new(authEnabled: true);

    private User _alice = null!;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.ClickEvents.RemoveRange(db.ClickEvents);
        db.ShortenedUrls.RemoveRange(db.ShortenedUrls);
        db.Domains.RemoveRange(db.Domains);
        db.Users.RemoveRange(db.Users);
        await db.SaveChangesAsync();

        _alice = new User
        {
            Issuer = ShortnrWebAppFactory.TestIssuer,
            Subject = "alice",
            CreatedAtUtc = DateTime.UtcNow,
            LastLoginAtUtc = DateTime.UtcNow
        };
        db.Users.Add(_alice);
        await db.SaveChangesAsync();

        var domain = new Domain
        {
            Hostname = "go.example.com",
            OwnerUserId = _alice.Id,
            IsVerified = true,
            VerificationToken = "tok",
            CreatedAtUtc = DateTime.UtcNow
        };
        db.Domains.Add(domain);
        await db.SaveChangesAsync();

        db.ShortenedUrls.AddRange(
            new ShortenedUrl { LongUrl = "https://example.com/default", ShortCode = "abc123", OwnerUserId = _alice.Id, CreatedAtUtc = DateTime.UtcNow },
            new ShortenedUrl { LongUrl = "https://example.com/custom", ShortCode = "xyz789", DomainId = domain.Id, OwnerUserId = _alice.Id, CreatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task SearchResults_ShowDomainColumn()
    {
        var client = AuthenticatedClient();
        var response = await client.SendAsync(HtmxGet("/dashboard", "search-results"));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("go.example.com/xyz789", html);
        Assert.Contains(">default<", html);
    }

    [Fact]
    public async Task SearchResults_FilterByCustomDomain_ShowsOnlyCustomDomainLinks()
    {
        var client = AuthenticatedClient();
        var response = await client.SendAsync(HtmxGet("/dashboard?domain=go.example.com", "search-results"));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("xyz789", html);
        Assert.DoesNotContain("abc123", html);
    }

    [Fact]
    public async Task SearchResults_FilterByDefault_ShowsOnlyDefaultDomainLinks()
    {
        var client = AuthenticatedClient();
        var response = await client.SendAsync(HtmxGet("/dashboard?domain=default", "search-results"));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("abc123", html);
        Assert.DoesNotContain("xyz789", html);
    }

    private HttpClient AuthenticatedClient()
    {
        _factory.Services.GetRequiredService<TestAuthState>().SetAuthenticatedUser("alice", ShortnrWebAppFactory.TestIssuer);
        return _factory.CreateClient();
    }

    private static HttpRequestMessage HtmxGet(string url, string target) =>
        new(HttpMethod.Get, url)
        {
            Headers = { { "HX-Request", "true" }, { "HX-Target", target } }
        };
}
