using System.Net;
using System.Text.Json;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Tests.Integration.Infrastructure;

namespace Shortnr.Tests.Integration.Api;

/// <summary>
/// Verifies that GET /api/metrics scopes results to the authenticated user
/// and returns aggregate-all behaviour when auth is disabled.
/// </summary>
public class MetricsEndpointTests : IAsyncLifetime
{
    private readonly ShortnrWebAppFactory _authEnabled = new(authEnabled: true);
    private readonly ShortnrWebAppFactory _authDisabled = new(authEnabled: false);

    private User _alice = null!;
    private User _bob = null!;

    public async Task InitializeAsync()
    {
        // Seed auth-enabled factory DB.
        using (var scope = _authEnabled.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ClickEvents.RemoveRange(db.ClickEvents);
            db.ShortenedUrls.RemoveRange(db.ShortenedUrls);
            db.Users.RemoveRange(db.Users);
            await db.SaveChangesAsync();

            _alice = new User { Issuer = ShortnrWebAppFactory.TestIssuer, Subject = "alice", CreatedAtUtc = DateTime.UtcNow, LastLoginAtUtc = DateTime.UtcNow };
            _bob = new User { Issuer = ShortnrWebAppFactory.TestIssuer, Subject = "bob", CreatedAtUtc = DateTime.UtcNow, LastLoginAtUtc = DateTime.UtcNow };
            db.Users.AddRange(_alice, _bob);
            await db.SaveChangesAsync();

            db.ShortenedUrls.AddRange(
                new ShortenedUrl { LongUrl = "https://alice.com/1", ShortCode = "aaa111", ClickCount = 4, OwnerUserId = _alice.Id, CreatedAtUtc = DateTime.UtcNow },
                new ShortenedUrl { LongUrl = "https://alice.com/2", ShortCode = "aaa222", ClickCount = 6, OwnerUserId = _alice.Id, CreatedAtUtc = DateTime.UtcNow },
                new ShortenedUrl { LongUrl = "https://bob.com/1", ShortCode = "bbb111", ClickCount = 9, OwnerUserId = _bob.Id, CreatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        // Seed auth-disabled factory DB (same links, no users).
        using (var scope = _authDisabled.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ClickEvents.RemoveRange(db.ClickEvents);
            db.ShortenedUrls.RemoveRange(db.ShortenedUrls);
            db.Users.RemoveRange(db.Users);
            await db.SaveChangesAsync();

            db.ShortenedUrls.AddRange(
                new ShortenedUrl { LongUrl = "https://alice.com/1", ShortCode = "aaa111", ClickCount = 4, CreatedAtUtc = DateTime.UtcNow },
                new ShortenedUrl { LongUrl = "https://alice.com/2", ShortCode = "aaa222", ClickCount = 6, CreatedAtUtc = DateTime.UtcNow },
                new ShortenedUrl { LongUrl = "https://bob.com/1", ShortCode = "bbb111", ClickCount = 9, CreatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
    }

    public async Task DisposeAsync()
    {
        await _authEnabled.DisposeAsync();
        await _authDisabled.DisposeAsync();
    }

    // -------------------------------------------------------------------------
    // Auth enabled — authenticated user sees only their own data
    // -------------------------------------------------------------------------

    [Fact]
    public async Task WhenAuthEnabled_AsAlice_ReturnsOnlyAliceMetrics()
    {
        var authState = _authEnabled.Services.GetRequiredService<TestAuthState>();
        authState.SetAuthenticatedUser("alice", ShortnrWebAppFactory.TestIssuer);

        var response = await _authEnabled.CreateClient().GetAsync("/api/metrics");
        var json = await ParseJson(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, json.GetProperty("totalLinks").GetInt32());
        Assert.Equal(10, json.GetProperty("totalClicks").GetInt64());
        Assert.Equal(0, json.GetProperty("totalCountries").GetInt32());
        // Top links should only be Alice's short codes.
        var codes = json.GetProperty("topLinks").EnumerateArray()
            .Select(l => l.GetProperty("shortCode").GetString())
            .ToList();
        Assert.All(codes, code => Assert.StartsWith("aaa", code!));
        AssertEmptyBreakdowns(json);
    }

    [Fact]
    public async Task WhenAuthEnabled_AsBob_ReturnsOnlyBobMetrics()
    {
        var authState = _authEnabled.Services.GetRequiredService<TestAuthState>();
        authState.SetAuthenticatedUser("bob", ShortnrWebAppFactory.TestIssuer);

        var response = await _authEnabled.CreateClient().GetAsync("/api/metrics");
        var json = await ParseJson(response);

        Assert.Equal(1, json.GetProperty("totalLinks").GetInt32());
        Assert.Equal(9, json.GetProperty("totalClicks").GetInt64());
        AssertEmptyBreakdowns(json);
    }

    [Fact]
    public async Task WhenAuthEnabled_Unauthenticated_ReturnsZeroMetrics()
    {
        _authEnabled.Services.GetRequiredService<TestAuthState>().ClearUser();

        var response = await _authEnabled.CreateClient().GetAsync("/api/metrics");
        var json = await ParseJson(response);

        Assert.Equal(0, json.GetProperty("totalLinks").GetInt32());
        Assert.Equal(0, json.GetProperty("totalClicks").GetInt64());
        Assert.Equal(0, json.GetProperty("totalCountries").GetInt32());
        AssertEmptyBreakdowns(json);
    }

    // -------------------------------------------------------------------------
    // Auth disabled — all data returned
    // -------------------------------------------------------------------------

    [Fact]
    public async Task WhenAuthDisabled_ReturnsAllMetrics()
    {
        var response = await _authDisabled.CreateClient().GetAsync("/api/metrics");
        var json = await ParseJson(response);

        Assert.Equal(3, json.GetProperty("totalLinks").GetInt32());
        Assert.Equal(19, json.GetProperty("totalClicks").GetInt64());
        AssertEmptyBreakdowns(json);
    }

    // -------------------------------------------------------------------------
    // Breakdown data
    // -------------------------------------------------------------------------

    [Fact]
    public async Task WhenClicksHaveEnrichedData_ReturnsBreakdowns()
    {
        using var factory = new ShortnrWebAppFactory(authEnabled: false);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ClickEvents.RemoveRange(db.ClickEvents);
            db.ShortenedUrls.RemoveRange(db.ShortenedUrls);
            await db.SaveChangesAsync();

            var url = new ShortenedUrl { LongUrl = "https://example.com", ShortCode = "abc123", ClickCount = 3, CreatedAtUtc = DateTime.UtcNow };
            db.ShortenedUrls.Add(url);
            await db.SaveChangesAsync();

            db.ClickEvents.AddRange(
                new ClickEvent { ShortenedUrlId = url.Id, IpAddress = "1.1.1.1", UserAgent = "Mozilla/5.0", ClickedAtUtc = DateTime.UtcNow, CountryCode = "US", CountryName = "United States", DeviceFamily = "Desktop", Browser = "Chrome", BrowserVersion = "128", OperatingSystem = "Windows", OSVersion = "10" },
                new ClickEvent { ShortenedUrlId = url.Id, IpAddress = "2.2.2.2", UserAgent = "Mozilla/5.0", ClickedAtUtc = DateTime.UtcNow, CountryCode = "US", CountryName = "United States", DeviceFamily = "Mobile", Browser = "Safari", BrowserVersion = "17", OperatingSystem = "iOS", OSVersion = "17" },
                new ClickEvent { ShortenedUrlId = url.Id, IpAddress = "3.3.3.3", UserAgent = "Mozilla/5.0", ClickedAtUtc = DateTime.UtcNow, CountryCode = "GB", CountryName = "United Kingdom", DeviceFamily = "Desktop", Browser = "Firefox", BrowserVersion = "130", OperatingSystem = "Windows", OSVersion = "11" });
            await db.SaveChangesAsync();
        }

        var response = await factory.CreateClient().GetAsync("/api/metrics");
        var json = await ParseJson(response);

        Assert.Equal(1, json.GetProperty("totalLinks").GetInt32());
        Assert.Equal(3, json.GetProperty("totalClicks").GetInt64());
        Assert.Equal(2, json.GetProperty("totalCountries").GetInt32());

        var countries = json.GetProperty("countryBreakdown").EnumerateArray().ToList();
        Assert.Contains(countries, c => c.GetProperty("countryCode").GetString() == "US" && c.GetProperty("count").GetInt32() == 2);
        Assert.Contains(countries, c => c.GetProperty("countryCode").GetString() == "GB" && c.GetProperty("count").GetInt32() == 1);

        var devices = json.GetProperty("deviceBreakdown").EnumerateArray().ToList();
        Assert.Contains(devices, d => d.GetProperty("label").GetString() == "Desktop" && d.GetProperty("count").GetInt32() == 2);
        Assert.Contains(devices, d => d.GetProperty("label").GetString() == "Mobile" && d.GetProperty("count").GetInt32() == 1);

        var browsers = json.GetProperty("browserBreakdown").EnumerateArray().ToList();
        Assert.Equal(3, browsers.Count);

        var oss = json.GetProperty("osBreakdown").EnumerateArray().ToList();
        Assert.Equal(2, oss.Count);
    }

    private static void AssertEmptyBreakdowns(JsonElement json)
    {
        Assert.Empty(json.GetProperty("countryBreakdown").EnumerateArray());
        Assert.Empty(json.GetProperty("deviceBreakdown").EnumerateArray());
        Assert.Empty(json.GetProperty("browserBreakdown").EnumerateArray());
        Assert.Empty(json.GetProperty("osBreakdown").EnumerateArray());
    }

    private static async Task<JsonElement> ParseJson(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(body).RootElement;
    }
}
