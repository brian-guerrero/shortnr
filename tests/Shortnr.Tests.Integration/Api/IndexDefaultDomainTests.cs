using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Tests.Integration.Infrastructure;

namespace Shortnr.Tests.Integration.Api;

/// <summary>
/// Verifies that the index page creates links on the signed-in owner's verified
/// default domain, and still uses the instance host when no default is set.
/// </summary>
public class IndexDefaultDomainTests : IAsyncLifetime
{
    private static readonly Regex AntiforgeryTokenRegex = new(
        @"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""",
        RegexOptions.Compiled);

    private readonly ShortnrWebAppFactory _factory = new(authEnabled: true);

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.ClickEvents.RemoveRange(db.ClickEvents);
        db.ShortenedUrls.RemoveRange(db.ShortenedUrls);
        db.Domains.RemoveRange(db.Domains);
        db.Users.RemoveRange(db.Users);
        await db.SaveChangesAsync();

        db.Users.Add(new User
        {
            Issuer = ShortnrWebAppFactory.TestIssuer,
            Subject = "alice",
            Email = "alice@example.com",
            CreatedAtUtc = DateTime.UtcNow,
            LastLoginAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    [Fact]
    public async Task PostWithoutSlug_WhenOwnerHasDefaultDomain_CreatesLinkOnThatDomain()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var alice = await db.Users.SingleAsync(u => u.Subject == "alice");
            db.Domains.Add(new Domain
            {
                Hostname = "go.example.com",
                OwnerUserId = alice.Id,
                IsVerified = true,
                IsDefault = true,
                VerificationToken = "tok",
                CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var client = AuthenticatedClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, token,
            ("url", "https://example.com/on-domain"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("go.example.com", body);

        using var scope2 = _factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var link = await db2.ShortenedUrls.SingleAsync(l => l.LongUrl == "https://example.com/on-domain");
        Assert.NotNull(link.DomainId);
    }

    [Fact]
    public async Task PostWithSlug_WhenOwnerHasDefaultDomain_CreatesLinkOnThatDomain()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var alice = await db.Users.SingleAsync(u => u.Subject == "alice");
            db.Domains.Add(new Domain
            {
                Hostname = "go.example.com",
                OwnerUserId = alice.Id,
                IsVerified = true,
                IsDefault = true,
                VerificationToken = "tok",
                CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var client = AuthenticatedClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, token,
            ("url", "https://example.com/on-domain-slug"),
            ("slug", "dom-slug"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope2 = _factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var link = await db2.ShortenedUrls.SingleAsync(l => l.ShortCode == "dom-slug");
        Assert.NotNull(link.DomainId);
    }

    [Fact]
    public async Task PostWithoutSlug_WhenNoDefaultDomain_UsesInstanceHost()
    {
        var client = AuthenticatedClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, token,
            ("url", "https://example.com/no-domain"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var link = await db.ShortenedUrls.SingleAsync(l => l.LongUrl == "https://example.com/no-domain");
        Assert.Null(link.DomainId);
    }

    [Fact]
    public async Task RecentLinks_ShowOwnersLinksAcrossDomains_WhenDefaultDomainIsSet()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var alice = await db.Users.SingleAsync(u => u.Subject == "alice");
            var domain = new Domain
            {
                Hostname = "go.example.com",
                OwnerUserId = alice.Id,
                IsVerified = true,
                IsDefault = true,
                VerificationToken = "tok",
                CreatedAtUtc = DateTime.UtcNow
            };
            db.Domains.Add(domain);
            await db.SaveChangesAsync();

            db.ShortenedUrls.AddRange(
                new ShortenedUrl { LongUrl = "https://example.com/old-instance", ShortCode = "old111", OwnerUserId = alice.Id, CreatedAtUtc = DateTime.UtcNow.AddMinutes(-2) },
                new ShortenedUrl { LongUrl = "https://example.com/old-domain", ShortCode = "old222", DomainId = domain.Id, OwnerUserId = alice.Id, CreatedAtUtc = DateTime.UtcNow.AddMinutes(-1) });
            await db.SaveChangesAsync();
        }

        var client = AuthenticatedClient();
        var page = await client.GetAsync("/");
        var html = await page.Content.ReadAsStringAsync();

        Assert.Contains("old111", html);
        Assert.Contains("old222", html);
        Assert.Contains("old-instance", html);
    }

    private HttpClient AuthenticatedClient()
    {
        _factory.Services.GetRequiredService<TestAuthState>()
            .SetAuthenticatedUser("alice", ShortnrWebAppFactory.TestIssuer);
        return _factory.CreateClient();
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        var page = await client.GetAsync("/");
        var html = await page.Content.ReadAsStringAsync();
        var match = AntiforgeryTokenRegex.Match(html);
        Assert.True(match.Success, "Antiforgery token not found in index page.");
        return match.Groups[1].Value;
    }

    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string token, params (string Name, string Value)[] fields)
    {
        var pairs = fields
            .Select(f => new KeyValuePair<string, string>(f.Name, f.Value))
            .ToList();
        pairs.Add(new KeyValuePair<string, string>("__RequestVerificationToken", token));
        return await client.PostAsync("/", new FormUrlEncodedContent(pairs));
    }
}
