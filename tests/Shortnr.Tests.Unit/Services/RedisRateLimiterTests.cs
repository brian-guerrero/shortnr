using System.Threading.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shortnr.Web.Features.Infrastructure;

namespace Shortnr.Tests.Unit.Services;

/// <summary>
/// PRD-017 unit coverage that needs no container: graceful-degradation fallback when Redis
/// is unreachable, Redis key construction/scoping, and the guarantee that the in-process
/// default (Provider=InProcess) still produces the PRD-010 limiter chain unchanged.
/// </summary>
public class RedisRateLimiterTests
{
    // Port 1 on loopback is never listening, so StackExchange connect fails fast instead of
    // hanging for the default 5s — the graceful-degradation case is exercised immediately.
    private const string DeadRedisConnectionString =
        "127.0.0.1:1,abortConnect=false,connectTimeout=250,syncTimeout=250";

    private static RedisConnectionProvider DeadRedisProvider() =>
        new(
            Options.Create(new RateLimitingOptions
            {
                Redis = new RedisRateLimitOptions { ConnectionString = DeadRedisConnectionString }
            }),
            NullLogger<RedisConnectionProvider>.Instance);

    private static RateLimitLimiterFactory FactoryWith(RateLimitProvider provider)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:Provider"] = provider.ToString(),
            })
            .Build();
        return new RateLimitLimiterFactory(
            config,
            new RedisConnectionProvider(
                Options.Create(new RateLimitingOptions()),
                NullLogger<RedisConnectionProvider>.Instance),
            NullLogger<RateLimitLimiterFactory>.Instance);
    }

    [Fact]
    public async Task AcquireAsync_RedisUnavailable_FallsBackToInProcess_DoesNotThrow()
    {
        var limiter = new RedisRateLimiter(DeadRedisProvider(), "test", "ip:1", 3, TimeSpan.FromMinutes(1), NullLogger.Instance);

        // First acquire must not throw even though Redis is a dead endpoint.
        using var lease = await limiter.AcquireAsync(1);

        Assert.True(lease.IsAcquired);
    }

    [Fact]
    public async Task AcquireAsync_RedisUnavailable_EnforcesLimitThroughFallback()
    {
        var limiter = new RedisRateLimiter(DeadRedisProvider(), "test", "ip:1", 2, TimeSpan.FromMinutes(1), NullLogger.Instance);

        using (await limiter.AcquireAsync(1))
        {
        }
        using (await limiter.AcquireAsync(1))
        {
        }
        using var third = await limiter.AcquireAsync(1);

        Assert.False(third.IsAcquired);
    }

    [Fact]
    public void FullKey_ScopesPolicyIdentifierAndWindow()
    {
        var limiter = new RedisRateLimiter(
            DeadRedisProvider(), "redirect-ip", "192.168.1.5", 10, TimeSpan.FromMinutes(1), NullLogger.Instance);

        Assert.Equal("shortnr:ratelimit:redirect-ip:192.168.1.5:60", limiter.FullKey);
    }

    [Fact]
    public async Task RejectedLease_ExposesRetryAfterMetadata()
    {
        var limiter = new RedisRateLimiter(DeadRedisProvider(), "test", "ip:1", 1, TimeSpan.FromMinutes(1), NullLogger.Instance);

        using (await limiter.AcquireAsync(1))
        {
        }
        using var rejected = await limiter.AcquireAsync(1);

        Assert.False(rejected.IsAcquired);
        Assert.Contains(rejected.MetadataNames, name => name == "RETRY_AFTER");
        Assert.True(rejected.TryGetMetadata("RETRY_AFTER", out var retryAfter));
        // Fallback lease carries the FixedWindowRateLimiter's retry-after (the full window).
        Assert.True((TimeSpan)retryAfter! > TimeSpan.Zero);
    }

    [Fact]
    public async Task InProcessDefault_BuildChain_EnforcesMinuteAndDayLikePrd010()
    {
        var limiter = FactoryWith(RateLimitProvider.InProcess).BuildChain(
            "redirect-ip", "ip:1", perMinute: 3, TimeSpan.FromMinutes(1), perDay: 30, TimeSpan.FromDays(1));

        // The default provider must still be a ChainedRateLimiter of FixedWindowRateLimiters
        // (i.e. the exact PRD-010 in-process construction, which the factory reproduces).
        Assert.IsType<ChainedRateLimiter>(limiter);

        var accepted = 0;
        for (var i = 0; i < 4; i++)
        {
            using var lease = await limiter.AcquireAsync(1);
            if (lease.IsAcquired)
                accepted++;
        }

        Assert.Equal(3, accepted);
        limiter.Dispose();
    }

    [Fact]
    public void ProviderHelper_Default_IsInProcess()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();

        Assert.Equal(RateLimitProvider.InProcess, RateLimitProviderHelper.ResolveProvider(config));
    }

    [Theory]
    [InlineData("InProcess", RateLimitProvider.InProcess)]
    [InlineData("in-process", RateLimitProvider.InProcess)]
    [InlineData("Redis", RateLimitProvider.Redis)]
    [InlineData("redis", RateLimitProvider.Redis)]
    public void ProviderHelper_AcceptsSupportedValues(string value, RateLimitProvider expected)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["RateLimiting:Provider"] = value })
            .Build();

        Assert.Equal(expected, RateLimitProviderHelper.ResolveProvider(config));
    }

    [Fact]
    public void ProviderHelper_UnknownValue_Throws()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["RateLimiting:Provider"] = "kafka" })
            .Build();

        Assert.Throws<InvalidOperationException>(() => RateLimitProviderHelper.ResolveProvider(config));
    }
}