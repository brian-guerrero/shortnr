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
        // Top links should only be Alice's short codes.
        var codes = json.GetProperty("topLinks").EnumerateArray()
            .Select(l => l.GetProperty("shortCode").GetString())
            .ToList();
        Assert.All(codes, code => Assert.StartsWith("aaa", code!));
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
    }

    [Fact]
    public async Task WhenAuthEnabled_Unauthenticated_ReturnsZeroMetrics()
    {
        // No TestAuthState.SetAuthenticatedUser call — anonymous request.
        _authEnabled.Services.GetRequiredService<TestAuthState>().ClearUser();

        var response = await _authEnabled.CreateClient().GetAsync("/api/metrics");
        var json = await ParseJson(response);

        // ownerUserId is null but auth is enabled → no user match → 0 results.
        Assert.Equal(0, json.GetProperty("totalLinks").GetInt32());
        Assert.Equal(0, json.GetProperty("totalClicks").GetInt64());
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
    }

    private static async Task<JsonElement> ParseJson(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(body).RootElement;
    }
}
