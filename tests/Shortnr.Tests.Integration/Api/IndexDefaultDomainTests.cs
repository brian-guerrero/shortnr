using System.Net;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Tests.Integration.Infrastructure;

namespace Shortnr.Tests.Integration.Api;

/// <summary>
/// Verifies that the index page creates links on the signed-in owner's verified
/// default domain, and still uses the instance host when no default is set.
/// </summary>
public class IndexDefaultDomainTests : IntegrationTestBase
{

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
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

    [Fact]
    public async Task PostWithoutSlug_WhenOwnerHasDefaultDomain_CreatesLinkOnThatDomain()
    {
        using (var scope = Factory.Services.CreateScope())
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

        using var scope2 = Factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var link = await db2.ShortenedUrls.SingleAsync(l => l.LongUrl == "https://example.com/on-domain");
        Assert.NotNull(link.DomainId);
    }

    [Fact]
    public async Task PostWithSlug_WhenOwnerHasDefaultDomain_CreatesLinkOnThatDomain()
    {
        using (var scope = Factory.Services.CreateScope())
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

        using var scope2 = Factory.Services.CreateScope();
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

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var link = await db.ShortenedUrls.SingleAsync(l => l.LongUrl == "https://example.com/no-domain");
        Assert.Null(link.DomainId);
    }

    [Fact]
    public async Task RecentLinks_ShowOwnersLinksAcrossDomains_WhenDefaultDomainIsSet()
    {
        using (var scope = Factory.Services.CreateScope())
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
        Factory.Services.GetRequiredService<TestAuthState>()
            .SetAuthenticatedUser("alice", ShortnrWebAppFactory.TestIssuer);
        return Factory.CreateClient();
    }

    private static Task<string> GetAntiforgeryTokenAsync(HttpClient client) =>
        TestForms.GetAntiforgeryTokenAsync(client, "/");

    private static Task<HttpResponseMessage> PostFormAsync(HttpClient client, string token, params (string Name, string Value)[] fields) =>
        TestForms.PostFormAsync(client, "/", token, fields);
}
