using System.Net;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Tests.Integration.Infrastructure;

namespace Shortnr.Tests.Integration.Dashboard;

/// <summary>
/// Verifies the dashboard's #recent-clicks table shows domain-scoped short URLs
/// (hostname/code) for links on custom domains, consistent with the link list.
/// </summary>
public class DashboardRecentClicksDomainTests : IAsyncLifetime
{
    private readonly ShortnrWebAppFactory _factory = new(authEnabled: false);

    public Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.ClickEvents.RemoveRange(db.ClickEvents);
        db.ShortenedUrls.RemoveRange(db.ShortenedUrls);
        db.Domains.RemoveRange(db.Domains);
        db.Users.RemoveRange(db.Users);

        var domain = new Domain
        {
            Hostname = "go.example.com",
            IsVerified = true,
            IsDefault = true,
            VerificationToken = "tok-abc",
            CreatedAtUtc = DateTime.UtcNow
        };
        db.Domains.Add(domain);
        db.SaveChanges();

        var domainLink = new ShortenedUrl
        {
            LongUrl = "https://example.com/domain",
            ShortCode = "dom001",
            DomainId = domain.Id,
            CreatedAtUtc = DateTime.UtcNow
        };
        var defaultLink = new ShortenedUrl
        {
            LongUrl = "https://example.com/default",
            ShortCode = "def001",
            CreatedAtUtc = DateTime.UtcNow
        };
        db.ShortenedUrls.AddRange(domainLink, defaultLink);
        db.SaveChanges();

        db.ClickEvents.AddRange(
            new ClickEvent { ShortenedUrlId = domainLink.Id, IpAddress = "1.1.1.1", ClickedAtUtc = DateTime.UtcNow },
            new ClickEvent { ShortenedUrlId = defaultLink.Id, IpAddress = "1.1.1.1", ClickedAtUtc = DateTime.UtcNow });
        db.SaveChanges();

        return Task.CompletedTask;
    }

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    private static HttpRequestMessage HtmxGet(string url, string target) =>
        new(HttpMethod.Get, url)
        {
            Headers = { { "HX-Request", "true" }, { "HX-Target", target } }
        };

    [Fact]
    public async Task RecentClicks_CustomDomainLink_ShowsHostnameCode()
    {
        var client = _factory.CreateClient();
        var response = await client.SendAsync(HtmxGet("/dashboard", "recent-clicks"));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("go.example.com/dom001", html);
    }

    [Fact]
    public async Task RecentClicks_DefaultHostLink_ShowsBareCode()
    {
        var client = _factory.CreateClient();
        var response = await client.SendAsync(HtmxGet("/dashboard", "recent-clicks"));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("/def001", html);
        Assert.DoesNotContain("localhost/def001", html);
    }
}
