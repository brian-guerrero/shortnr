using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Tests.Integration.Infrastructure;
using Shortnr.Web.Features.Infrastructure;
using StackExchange.Redis;
using Xunit;

namespace Shortnr.Tests.Integration.RateLimiting;

/// <summary>
/// PRD-017 distributed rate limiting: a real Redis instance (Testcontainers) backing the
/// opt-in <c>RateLimiting:Provider=Redis</c> store. Covers basic rate limiting across the
/// redirect/shorten/api-key surfaces, TTL semantics, key scoping, graceful degradation when
/// Redis is killed mid-test, and the <c>/health/redis</c> endpoint. Skips (never fails) when
/// Docker isn't available — CI's runners exercise these for real.
/// </summary>
[Collection("Redis")]
[Trait("Category", "Redis")]
public class RedisRateLimitTests(RedisContainerFixture RedisFixture) : IAsyncLifetime
{
    private static readonly Regex AntiforgeryTokenRegex = new(
        @"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""",
        RegexOptions.Compiled);

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => Task.CompletedTask;

    private static string UniqueCode() => $"rl{Guid.NewGuid():N}"[..10];

    private RedisRateLimitFactory CreateFactory(
        int database,
        bool authEnabled = false,
        int redirectPerMinute = 100000,
        int redirectPerDay = 1000000,
        int shortenPerMinute = 100000,
        int shortenPerDay = 1000000) =>
        new(RedisFixture.GetConnectionString(database), authEnabled)
        {
            RedirectPerMinute = redirectPerMinute,
            RedirectPerDay = redirectPerDay,
            ShortenPerMinute = shortenPerMinute,
            ShortenPerDay = shortenPerDay
        };

    private RedisConnectionProvider CreateProvider(int database) =>
        new(
            Options.Create(new RateLimitingOptions
            {
                Redis = new RedisRateLimitOptions { ConnectionString = RedisFixture.GetConnectionString(database) }
            }),
            NullLogger<RedisConnectionProvider>.Instance);

    private static async Task SeedLinkAsync(ShortnrWebAppFactory factory, string code)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.ShortenedUrls.Add(new ShortenedUrl
        {
            LongUrl = "https://example.com/redis-target",
            ShortCode = code,
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private async Task HelloRedisAsync(int timeoutMs = 10_000)
    {
        // Primer: make sure the shared container is actually reachable before a test assumes
        // Redis semantics rather than the in-process fallback.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var probe = ConnectionMultiplexer.Connect(RedisFixture.GetConnectionString());
                if (probe.IsConnected)
                    return;
            }
            catch (RedisException)
            {
            }
            if (attempt * 500 > timeoutMs)
                throw new TimeoutException("Redis container did not become reachable in time.");
            await Task.Delay(500);
        }
    }

    // ── basic rate limiting ─────────────────────────────────────────────────────

    [SkippableFact]
    public async Task RedirectEndpoint_AfterPerMinuteCap_Returns429()
    {
        Skip.If(!RedisFixture.IsAvailable, RedisFixture.UnavailableReason);
        await HelloRedisAsync();

        var database = RedisFixture.NextDatabase();
        await using var factory = CreateFactory(database, redirectPerMinute: 3, redirectPerDay: 3);
        var code = UniqueCode();
        await SeedLinkAsync(factory, code);

        var client = factory.CreateClientNoRedirect();
        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.Found, (await client.GetAsync($"/{code}")).StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync($"/{code}")).StatusCode);
    }

    [SkippableFact]
    public async Task ShortenForm_AfterPerMinuteCap_Returns429()
    {
        Skip.If(!RedisFixture.IsAvailable, RedisFixture.UnavailableReason);
        await HelloRedisAsync();

        var database = RedisFixture.NextDatabase();
        await using var factory = CreateFactory(database, shortenPerMinute: 3, shortenPerDay: 3);

        var client = factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.OK,
                (await client.PostAsync("/", BuildForm(token, ("url", $"https://example.com/{i}")))).StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests,
            (await client.PostAsync("/", BuildForm(token, ("url", "https://example.com/over")))).StatusCode);
    }

    [SkippableFact]
    public async Task ApiKeyEndpoint_AfterMinuteBurstCap_Returns429()
    {
        Skip.If(!RedisFixture.IsAvailable, RedisFixture.UnavailableReason);
        await HelloRedisAsync();

        // api-key policy limits are hardcoded (60/min) in Program.cs; this test just proves
        // the Redis-backed limiter enforces them like the in-process one does.
        const string key = "snr_redistestkey1234567890abcdef1234";
        var database = RedisFixture.NextDatabase();
        await using var factory = CreateFactory(database, authEnabled: true);

        await SeedApiKeyAsync(factory, "redis-api-owner", key);

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);

        HttpStatusCode last = default;
        for (var i = 0; i < 61; i++)
            last = (await client.GetAsync("/api/v1/links")).StatusCode;

        Assert.Equal(HttpStatusCode.TooManyRequests, last);
    }

    // ── distributed enforcement across "containers" ─────────────────────────────

    [SkippableFact]
    public async Task TwoInstances_SharingRedis_EnforceCombinedLimit()
    {
        Skip.If(!RedisFixture.IsAvailable, RedisFixture.UnavailableReason);
        await HelloRedisAsync();

        var database = RedisFixture.NextDatabase();
        await using var instanceA = CreateFactory(database, redirectPerMinute: 3, redirectPerDay: 3);
        await using var instanceB = CreateFactory(database, redirectPerMinute: 3, redirectPerDay: 3);

        var code = UniqueCode();
        await SeedLinkAsync(instanceA, code);
        await SeedLinkAsync(instanceB, code);

        var clientA = instanceA.CreateClientNoRedirect();
        var clientB = instanceB.CreateClientNoRedirect();

        // Three redirects, spread across two independent app instances but both counting into
        // the same Redis key (same client IP -> same partition key). The 4th is rejected,
        // proving the limit is cluster-wide and not per-container.
        Assert.Equal(HttpStatusCode.Found, (await clientA.GetAsync($"/{code}")).StatusCode);
        Assert.Equal(HttpStatusCode.Found, (await clientB.GetAsync($"/{code}")).StatusCode);
        Assert.Equal(HttpStatusCode.Found, (await clientA.GetAsync($"/{code}")).StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await clientB.GetAsync($"/{code}")).StatusCode);
    }

    // ── TTL / window expiry ──────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Counter_ExpiresAfterWindow_AllowsRequestsThroughAgain()
    {
        Skip.If(!RedisFixture.IsAvailable, RedisFixture.UnavailableReason);
        await HelloRedisAsync();

        var database = RedisFixture.NextDatabase();

        // Rejected requests within the window carry RETRY_AFTER and only a fixed window of
        // Redis budget, matching the in-process FixedWindowRateLimiter's metadata surface.
        var limiter = new RedisRateLimiter(
            CreateProvider(database), "test", "client-1", permitLimit: 1, TimeSpan.FromSeconds(2), NullLogger.Instance);
        try
        {
            {
                using var first = await limiter.AcquireAsync(1);
                Assert.True(first.IsAcquired);
            }

            using (var urgent = await limiter.AcquireAsync(1))
            {
                Assert.False(urgent.IsAcquired);
                Assert.True(urgent.TryGetMetadata("RETRY_AFTER", out var retryAfter));
                Assert.InRange((TimeSpan)retryAfter!, TimeSpan.Zero, TimeSpan.FromSeconds(2));
            }

            await Task.Delay(2500);

            using var afterExpiry = await limiter.AcquireAsync(1);
            Assert.True(afterExpiry.IsAcquired);
        }
        finally
        {
            limiter.Dispose();
        }
    }

    [SkippableFact]
    public async Task CounterKey_HasTtlMatchingWindow()
    {
        Skip.If(!RedisFixture.IsAvailable, RedisFixture.UnavailableReason);
        await HelloRedisAsync();

        var database = RedisFixture.NextDatabase();
        var limiter = new RedisRateLimiter(
            CreateProvider(database), "test", "client-2", permitLimit: 2, TimeSpan.FromSeconds(10), NullLogger.Instance);

        using var lease = await limiter.AcquireAsync(1);
        Assert.True(lease.IsAcquired);

        var db = CreateProvider(database).GetDatabase();
        Assert.True(db.KeyExists(limiter.FullKey));
        var ttl = db.KeyTimeToLive(limiter.FullKey);
        Assert.NotNull(ttl);
        Assert.InRange(ttl!.Value, TimeSpan.Zero, TimeSpan.FromSeconds(10));
    }

    // ── key scoping ──────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task KeyScoping_DifferentIdentifiers_AreIndependent()
    {
        Skip.If(!RedisFixture.IsAvailable, RedisFixture.UnavailableReason);
        await HelloRedisAsync();

        var database = RedisFixture.NextDatabase();
        var provider = CreateProvider(database);
        var alice = new RedisRateLimiter(provider, "redirect-ip", "10.0.0.1", 2, TimeSpan.FromMinutes(1), NullLogger.Instance);
        var bob = new RedisRateLimiter(provider, "redirect-ip", "10.0.0.2", 2, TimeSpan.FromMinutes(1), NullLogger.Instance);

        using (await alice.AcquireAsync(1)) { }
        using (await alice.AcquireAsync(1)) { }
        using var aliceThird = await alice.AcquireAsync(1);
        Assert.False(aliceThird.IsAcquired);

        // Bob's counter is untouched by Alice saturating hers.
        using var bobFirst = await bob.AcquireAsync(1);
        Assert.True(bobFirst.IsAcquired);

        Assert.Equal("shortnr:ratelimit:redirect-ip:10.0.0.1:60", alice.FullKey);
        Assert.Equal("shortnr:ratelimit:redirect-ip:10.0.0.2:60", bob.FullKey);
    }

    [SkippableFact]
    public async Task KeyScoping_MinuteAndDayWindows_DontShareCounters()
    {
        Skip.If(!RedisFixture.IsAvailable, RedisFixture.UnavailableReason);
        await HelloRedisAsync();

        var database = RedisFixture.NextDatabase();
        var provider = CreateProvider(database);
        // Same policy/identifier, different windows -> different keys, else a bare INCR shared
        // between the minute burst and day cap would break the minute semantics.
        var minute = new RedisRateLimiter(provider, "api-key", "abc", 1, TimeSpan.FromMinutes(1), NullLogger.Instance);
        var day = new RedisRateLimiter(provider, "api-key", "abc", 1000, TimeSpan.FromDays(1), NullLogger.Instance);

        Assert.NotEqual(minute.FullKey, day.FullKey);
        Assert.Contains(":60", minute.FullKey);
        Assert.Contains(":86400", day.FullKey);

        using var minuteLease = await minute.AcquireAsync(1);
        using var dayLease = await day.AcquireAsync(1);
        Assert.True(minuteLease.IsAcquired);
        Assert.True(dayLease.IsAcquired);
    }

    [SkippableFact]
    public async Task HttpTraffic_WritesKeysUnderShortnrRateLimitNamespace()
    {
        Skip.If(!RedisFixture.IsAvailable, RedisFixture.UnavailableReason);
        await HelloRedisAsync();

        var database = RedisFixture.NextDatabase();
        await using var factory = CreateFactory(database, redirectPerMinute: 10, redirectPerDay: 10);
        var code = UniqueCode();
        await SeedLinkAsync(factory, code);

        var client = factory.CreateClientNoRedirect();
        Assert.Equal(HttpStatusCode.Found, (await client.GetAsync($"/{code}")).StatusCode);

        using var inspect = ConnectionMultiplexer.Connect(RedisFixture.GetConnectionString(database));
        var server = inspect.GetServer(inspect.GetEndPoints().Single());
        var keys = server.Keys(pattern: "shortnr:ratelimit:*")
            .Select(k => (string)k!)
            .ToList();

        Assert.NotEmpty(keys);
        Assert.All(keys, key => Assert.StartsWith("shortnr:ratelimit:", key));
        // The redirect policy for the loopback client must be among them.
        Assert.Contains(keys, key => key.StartsWith("shortnr:ratelimit:redirect-ip:", StringComparison.Ordinal));
    }

    // ── graceful degradation ─────────────────────────────────────────────────────

    [SkippableFact]
    public async Task KillingRedis_DegradesToInProcess_No500AndRecovers()
    {
        Skip.If(!RedisFixture.IsAvailable, RedisFixture.UnavailableReason);
        await HelloRedisAsync();

        var database = RedisFixture.NextDatabase();
        await using var factory = CreateFactory(database, redirectPerMinute: 3, redirectPerDay: 3);
        var code = UniqueCode();
        await SeedLinkAsync(factory, code);
        var client = factory.CreateClientNoRedirect();

        // Warm up the partition limiter + Redis connection while Redis is up.
        Assert.Equal(HttpStatusCode.Found, (await client.GetAsync($"/{code}")).StatusCode);
        Assert.Equal(HttpStatusCode.Found, (await client.GetAsync($"/{code}")).StatusCode);

        try
        {
            // Kill Redis while the app is live; the next batch of requests must NOT 500.
            await RedisFixture.StopAsync();
            await Task.Delay(500);

            for (var i = 0; i < 3; i++)
            {
                var response = await client.GetAsync($"/{code}");
                Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
                // Either the in-process fallback (302, still within its own counter) or — if
                // Redis managed one last write before the socket died — a 429. Never a 500.
                Assert.Contains((int)response.StatusCode, new[] { 302, 429 });
            }
        }
        finally
        {
            await RedisFixture.StartAsync();
        }

        // After Redis returns, the app keeps serving (connection re-establishes).
        await HelloRedisAsync();
        var recovered = await client.GetAsync($"/{code}");
        Assert.NotEqual(HttpStatusCode.InternalServerError, recovered.StatusCode);
        Assert.Contains((int)recovered.StatusCode, new[] { 302, 429 });
    }

    // ── health endpoint ──────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task HealthRedis_ReportsHealthy_WhenRedisReachable()
    {
        Skip.If(!RedisFixture.IsAvailable, RedisFixture.UnavailableReason);
        await HelloRedisAsync();

        var database = RedisFixture.NextDatabase();
        await using var factory = CreateFactory(database);

        var response = await factory.CreateClient().GetAsync("/health/redis");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Healthy", await response.Content.ReadAsStringAsync());
    }

    [SkippableFact]
    public async Task HealthRedis_ReportsUnhealthy_WhenRedisStopped()
    {
        Skip.If(!RedisFixture.IsAvailable, RedisFixture.UnavailableReason);
        await HelloRedisAsync();

        var database = RedisFixture.NextDatabase();
        await using var factory = CreateFactory(database);
        var client = factory.CreateClient();

        try
        {
            // Point the app at a dead endpoint by killing the shared container mid-flight.
            await RedisFixture.StopAsync();
            await Task.Delay(500);

            var response = await client.GetAsync("/health/redis");

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Contains("Unhealthy", await response.Content.ReadAsStringAsync());
        }
        finally
        {
            await RedisFixture.StartAsync();
        }
    }

    [Fact]
    public async Task HealthRedis_NotMapped_WhenProviderIsInProcess()
    {
        // The Redis health endpoint is opt-in with the provider; the default InProcess
        // deployment must not expose it.
        await using var factory = new ShortnrWebAppFactory(authEnabled: false);

        var response = await factory.CreateClient().GetAsync("/health/redis");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── helpers ──────────────────────────────────────────────────────────────────

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        var page = await client.GetAsync("/");
        var html = await page.Content.ReadAsStringAsync();
        var match = AntiforgeryTokenRegex.Match(html);
        Assert.True(match.Success, "Antiforgery token not found in index page.");
        return match.Groups[1].Value;
    }

    private static FormUrlEncodedContent BuildForm(string token, params (string Name, string Value)[] fields)
    {
        var pairs = fields
            .Select(f => new KeyValuePair<string, string>(f.Name, f.Value))
            .ToList();
        pairs.Add(new KeyValuePair<string, string>("__RequestVerificationToken", token));
        return new FormUrlEncodedContent(pairs);
    }

    private static async Task SeedApiKeyAsync(ShortnrWebAppFactory factory, string subject, string key)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new User
        {
            Issuer = ShortnrWebAppFactory.TestIssuer,
            Subject = subject,
            CreatedAtUtc = DateTime.UtcNow,
            LastLoginAtUtc = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.ApiKeys.Add(new ApiKey
        {
            OwnerUserId = user.Id,
            KeyHash = ApiKeyService.HashKey(key),
            KeyPrefix = "snr_",
            Label = "redis test key",
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// A Redis-backed factory with per-instance rate-limit overrides so tests can drive
    /// low limits without the base factory's generous defaults.
    /// </summary>
    private sealed class RedisRateLimitFactory(string connectionString, bool authEnabled) : ShortnrWebAppFactory(authEnabled)
    {
        public int RedirectPerMinute { get; init; } = 100000;
        public int RedirectPerDay { get; init; } = 1000000;
        public int ShortenPerMinute { get; init; } = 100000;
        public int ShortenPerDay { get; init; } = 1000000;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["RateLimiting:Provider"] = "Redis",
                    ["RateLimiting:Redis:ConnectionString"] = connectionString,
                    ["RateLimiting:Redirect:PerMinute"] = RedirectPerMinute.ToString(),
                    ["RateLimiting:Redirect:PerDay"] = RedirectPerDay.ToString(),
                    ["RateLimiting:Shorten:PerMinute"] = ShortenPerMinute.ToString(),
                    ["RateLimiting:Shorten:PerDay"] = ShortenPerDay.ToString(),
                }));
        }
    }
}