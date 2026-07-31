using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Tests.Integration.Infrastructure;
using Shortnr.Web.Services;

namespace Shortnr.Tests.Integration.Settings;

/// <summary>
/// Verifies the /settings/domains management page: add, verify (via a stubbed
/// DomainVerifierService), delete, owner scoping and auth gating.
/// </summary>
public class DomainsSettingsTests : IAsyncLifetime
{
    private static readonly Regex AntiforgeryTokenRegex = new(
        @"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""",
        RegexOptions.Compiled);

    /// <summary>Tokens the stub verifier claims to serve, keyed by hostname.</summary>
    private static readonly Dictionary<string, string> ServedTokens = new();

    /// <summary>TXT records the stub DNS resolver claims to hold, keyed by record name.</summary>
    private static readonly Dictionary<string, string> ServedTxtRecords = new();

    private readonly StubVerifierFactory _factory = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    [Fact]
    public async Task AddDomain_AppearsUnverified_AndWellKnownServesToken()
    {
        var client = _factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/settings/domains", token,
            ("hostname", "go.example.com"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("go.example.com", body);
        Assert.Contains("unverified", body);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var domain = await db.Domains.SingleAsync(d => d.Hostname == "go.example.com");
        Assert.False(domain.IsVerified);
        Assert.False(string.IsNullOrEmpty(domain.VerificationToken));

        var wellKnown = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/.well-known/shortnr-verify.txt")
        {
            Headers = { Host = "go.example.com" }
        });
        Assert.Equal(HttpStatusCode.OK, wellKnown.StatusCode);
        Assert.Equal(domain.VerificationToken, (await wellKnown.Content.ReadAsStringAsync()).Trim());
    }

    [Fact]
    public async Task AddDuplicateHostname_ReturnsError()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Domains.Add(new Domain
            {
                Hostname = "go.example.com",
                VerificationToken = "tok",
                CreatedAtUtc = DateTime.UtcNow
            });
            db.SaveChanges();
        }

        var client = _factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/settings/domains", token,
            ("hostname", "go.example.com"));

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("already registered", body);

        using var scope2 = _factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db2.Domains.CountAsync(d => d.Hostname == "go.example.com"));
    }

    [Fact]
    public async Task AddInvalidHostname_ReturnsValidationError()
    {
        var client = _factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/settings/domains", token,
            ("hostname", "not a domain"));

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("valid domain name", body);
    }

    [Fact]
    public async Task VerifyDomain_WhenTokenMatches_MarksVerified()
    {
        ServedTokens.Clear();
        ServedTokens["go.example.com"] = "TOKEN-ABC";
        var domainId = await SeedDomainAsync("go.example.com", "TOKEN-ABC", verified: false);

        var client = _factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/settings/domains?handler=Verify", token,
            ("id", domainId.ToString()));

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("verified", body);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True((await db.Domains.SingleAsync(d => d.Id == domainId)).IsVerified);
    }

    [Fact]
    public async Task VerifyDomain_ByTxt_WhenRecordMatches_MarksVerified()
    {
        ServedTxtRecords.Clear();
        ServedTxtRecords["_shortnr-verify.go.example.com"] = "TOKEN-TXT";
        var domainId = await SeedDomainAsync("go.example.com", "TOKEN-TXT", verified: false);

        var client = _factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/settings/domains?handler=Verify", token,
            ("id", domainId.ToString()),
            ("method", "txt"));

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("verified", body);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True((await db.Domains.SingleAsync(d => d.Id == domainId)).IsVerified);
    }

    [Fact]
    public async Task VerifyDomain_ByTxt_WhenRecordMissing_StaysUnverified()
    {
        ServedTxtRecords.Clear();
        var domainId = await SeedDomainAsync("go.example.com", "TOKEN-TXT", verified: false);

        var client = _factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/settings/domains?handler=Verify", token,
            ("id", domainId.ToString()),
            ("method", "txt"));

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Verification failed", body);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False((await db.Domains.SingleAsync(d => d.Id == domainId)).IsVerified);
    }

    [Fact]
    public async Task VerifyDomain_FirstVerifiedDomain_BecomesDefault()
    {
        ServedTokens.Clear();
        ServedTokens["go.example.com"] = "TOKEN-ABC";
        var domainId = await SeedDomainAsync("go.example.com", "TOKEN-ABC", verified: false);

        var client = _factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/settings/domains?handler=Verify", token,
            ("id", domainId.ToString()));

        Assert.Contains("default domain", await response.Content.ReadAsStringAsync());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var domain = await db.Domains.SingleAsync(d => d.Id == domainId);
        Assert.True(domain.IsVerified);
        Assert.True(domain.IsDefault);
    }

    [Fact]
    public async Task VerifySecondDomain_DoesNotStealDefault()
    {
        ServedTokens.Clear();
        ServedTokens["one.example.com"] = "TOKEN-1";
        ServedTokens["two.example.com"] = "TOKEN-2";
        var firstId = await SeedDomainAsync("one.example.com", "TOKEN-1", verified: true);
        await SetDefaultAsync(firstId);
        var secondId = await SeedDomainAsync("two.example.com", "TOKEN-2", verified: false);

        var client = _factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/settings/domains?handler=Verify", token,
            ("id", secondId.ToString()));

        Assert.DoesNotContain("default domain", await response.Content.ReadAsStringAsync());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True((await db.Domains.SingleAsync(d => d.Id == firstId)).IsDefault);
        Assert.False((await db.Domains.SingleAsync(d => d.Id == secondId)).IsDefault);
    }

    [Fact]
    public async Task SetDefault_WhenVerified_MarksItDefaultAndClearsOthers()
    {
        var firstId = await SeedDomainAsync("one.example.com", "TOKEN-1", verified: true);
        await SetDefaultAsync(firstId);
        var secondId = await SeedDomainAsync("two.example.com", "TOKEN-2", verified: true);

        var client = _factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/settings/domains?handler=SetDefault", token,
            ("id", secondId.ToString()));

        Assert.Contains("now the default", await response.Content.ReadAsStringAsync());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False((await db.Domains.SingleAsync(d => d.Id == firstId)).IsDefault);
        Assert.True((await db.Domains.SingleAsync(d => d.Id == secondId)).IsDefault);
    }

    [Fact]
    public async Task SetDefault_WhenUnverified_ReturnsError()
    {
        var domainId = await SeedDomainAsync("go.example.com", "TOKEN-ABC", verified: false);

        var client = _factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/settings/domains?handler=SetDefault", token,
            ("id", domainId.ToString()));

        Assert.Contains("Only verified domains", await response.Content.ReadAsStringAsync());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False((await db.Domains.SingleAsync(d => d.Id == domainId)).IsDefault);
    }

    [Fact]
    public async Task SetDefault_MigratesExistingNoDomainLinksToTheDefaultDomain()
    {
        var domainId = await SeedDomainAsync("go.example.com", "TOKEN-ABC", verified: true);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ShortenedUrls.Add(new ShortenedUrl
            {
                LongUrl = "https://example.com/migrate-me",
                ShortCode = "mig001",
                CreatedAtUtc = DateTime.UtcNow
            });
            db.SaveChanges();
        }

        var client = _factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/settings/domains?handler=SetDefault", token,
            ("id", domainId.ToString()));

        Assert.Contains("now the default", await response.Content.ReadAsStringAsync());

        using var scope2 = _factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var link = await db2.ShortenedUrls.SingleAsync(l => l.ShortCode == "mig001");
        Assert.Equal(domainId, link.DomainId);
    }

    [Fact]
    public async Task VerifyFirstDomain_MigratesExistingNoDomainLinksToIt()
    {
        ServedTokens.Clear();
        ServedTokens["go.example.com"] = "TOKEN-ABC";
        var domainId = await SeedDomainAsync("go.example.com", "TOKEN-ABC", verified: false);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ShortenedUrls.Add(new ShortenedUrl
            {
                LongUrl = "https://example.com/migrate-me",
                ShortCode = "mig002",
                CreatedAtUtc = DateTime.UtcNow
            });
            db.SaveChanges();
        }

        var client = _factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/settings/domains?handler=Verify", token,
            ("id", domainId.ToString()));

        Assert.Contains("verified and set as the default", await response.Content.ReadAsStringAsync());

        using var scope2 = _factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var link = await db2.ShortenedUrls.SingleAsync(l => l.ShortCode == "mig002");
        Assert.Equal(domainId, link.DomainId);
    }

    [Fact]
    public async Task SetDefault_DoesNotMigrateLinksAlreadyOnAnotherDomain()
    {
        var defaultId = await SeedDomainAsync("go.example.com", "TOKEN-ABC", verified: true);
        var otherId = await SeedDomainAsync("other.example.com", "TOKEN-OTHER", verified: true);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ShortenedUrls.Add(new ShortenedUrl
            {
                LongUrl = "https://example.com/stays",
                ShortCode = "stay001",
                DomainId = otherId,
                CreatedAtUtc = DateTime.UtcNow
            });
            db.SaveChanges();
        }

        var client = _factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/settings/domains?handler=SetDefault", token,
            ("id", defaultId.ToString()));

        Assert.Contains("now the default", await response.Content.ReadAsStringAsync());

        using var scope2 = _factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var link = await db2.ShortenedUrls.SingleAsync(l => l.ShortCode == "stay001");
        Assert.Equal(otherId, link.DomainId);
    }

    [Fact]
    public async Task VerifyDomain_WhenTokenMissing_StaysUnverified()
    {
        ServedTokens.Clear();
        var domainId = await SeedDomainAsync("go.example.com", "TOKEN-ABC", verified: false);

        var client = _factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/settings/domains?handler=Verify", token,
            ("id", domainId.ToString()));

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Verification failed", body);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False((await db.Domains.SingleAsync(d => d.Id == domainId)).IsVerified);
    }

    [Fact]
    public async Task DeleteDomain_RemovesIt_AndLinksFallBackToDefaultDomain()
    {
        var domainId = await SeedDomainAsync("go.example.com", "TOKEN-ABC", verified: true);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ShortenedUrls.Add(new ShortenedUrl
            {
                LongUrl = "https://example.com/x",
                ShortCode = "abc123",
                DomainId = domainId,
                CreatedAtUtc = DateTime.UtcNow
            });
            db.SaveChanges();
        }

        var client = _factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/settings/domains?handler=Delete", token,
            ("id", domainId.ToString()));

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("removed", body);

        using var scope2 = _factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await db2.Domains.CountAsync());
        var link = await db2.ShortenedUrls.SingleAsync(l => l.ShortCode == "abc123");
        Assert.Null(link.DomainId);
    }

    [Fact]
    public async Task WhenAuthEnabled_UnauthenticatedFullPage_RedirectsToIndex()
    {
        using var authFactory = new StubVerifierFactory(authEnabled: true);
        var client = authFactory.CreateClientNoRedirect();

        var response = await client.GetAsync("/settings/domains");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task WhenAuthEnabled_UnauthenticatedHtmx_ReturnsUnauthorized()
    {
        using var authFactory = new StubVerifierFactory(authEnabled: true);
        var client = authFactory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/settings/domains");
        request.Headers.Add("HX-Request", "true");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DomainsAreScopedToOwner()
    {
        using var authFactory = new StubVerifierFactory(authEnabled: true);
        using (var scope = authFactory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var alice = new User { Issuer = ShortnrWebAppFactory.TestIssuer, Subject = "alice", CreatedAtUtc = DateTime.UtcNow, LastLoginAtUtc = DateTime.UtcNow };
            var bob = new User { Issuer = ShortnrWebAppFactory.TestIssuer, Subject = "bob", CreatedAtUtc = DateTime.UtcNow, LastLoginAtUtc = DateTime.UtcNow };
            db.Users.AddRange(alice, bob);
            db.SaveChanges();

            db.Domains.Add(new Domain
            {
                Hostname = "bob.example.com",
                OwnerUserId = bob.Id,
                VerificationToken = "tok-bob",
                CreatedAtUtc = DateTime.UtcNow
            });
            db.SaveChanges();
        }

        authFactory.Services.GetRequiredService<TestAuthState>().SetAuthenticatedUser("alice", ShortnrWebAppFactory.TestIssuer);

        var response = await authFactory.CreateClient().GetAsync("/settings/domains");
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("bob.example.com", body);
    }

    private async Task<long> SeedDomainAsync(string hostname, string token, bool verified)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var domain = new Domain
        {
            Hostname = hostname,
            IsVerified = verified,
            VerificationToken = token,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.Domains.Add(domain);
        await db.SaveChangesAsync();
        return domain.Id;
    }

    private async Task SetDefaultAsync(long domainId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var domain = await db.Domains.SingleAsync(d => d.Id == domainId);
        domain.IsDefault = true;
        await db.SaveChangesAsync();
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        var page = await client.GetAsync("/settings/domains");
        var html = await page.Content.ReadAsStringAsync();
        var match = AntiforgeryTokenRegex.Match(html);
        Assert.True(match.Success, "Antiforgery token not found in settings page.");
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

    private sealed class StubVerifierFactory(bool authEnabled = false) : ShortnrWebAppFactory(authEnabled)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DomainVerifierService>();
                services.AddScoped<DomainVerifierService>(_ =>
                    new DomainVerifierService(
                        new HttpClient(new StubVerifierHandler()),
                        new StubTxtDnsResolver()));
            });
        }
    }

    private sealed class StubVerifierHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var host = request.RequestUri?.Host ?? "";
            if (ServedTokens.TryGetValue(host, out var token))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(token) });

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class StubTxtDnsResolver : ITxtDnsResolver
    {
        public Task<IReadOnlyList<string>> GetTxtRecordsAsync(string name, CancellationToken cancellationToken = default)
        {
            var token = ServedTxtRecords.TryGetValue(name, out var value) ? value : null;
            return Task.FromResult<IReadOnlyList<string>>(token is null ? [] : [token]);
        }
    }
}
