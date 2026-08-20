using System.Threading.RateLimiting;
using Microsoft.Extensions.Configuration;

namespace Shortnr.Web.Features.Infrastructure;

/// <summary>
/// Builds the chained per-key rate limiters (per-minute burst + per-day cap) used by every
/// policy, honouring <c>RateLimiting:Provider</c>. With <see cref="RateLimitProvider.InProcess"/>
/// (the zero-config default) it produces exactly the PRD-010 <see cref="FixedWindowRateLimiter"/>
/// chain; with <see cref="RateLimitProvider.Redis"/> the same chain is built from
/// <see cref="RedisRateLimiter"/> so counters are shared cluster-wide (PRD-017).
/// </summary>
public sealed class RateLimitLimiterFactory
{
    private readonly RateLimitProvider _provider;
    private readonly RedisConnectionProvider _redis;
    private readonly ILogger<RateLimitLimiterFactory> _logger;

    public RateLimitLimiterFactory(
        IConfiguration configuration,
        RedisConnectionProvider redis,
        ILogger<RateLimitLimiterFactory> logger)
    {
        _provider = RateLimitProviderHelper.ResolveProvider(configuration);
        _redis = redis;
        _logger = logger;
    }

    /// <summary>
    /// Builds a per-minute burst window stacked with a per-day cap for <paramref name="policy"/>.
    /// <paramref name="identifier"/> is the partition key (hashed API key, client IP, ...) and
    /// scopes the Redis keys so each identifier is limited independently.
    /// </summary>
    public RateLimiter BuildChain(
        string policy,
        string identifier,
        int perMinute,
        TimeSpan minuteWindow,
        int perDay,
        TimeSpan dayWindow)
    {
        if (_provider == RateLimitProvider.Redis)
        {
            return new ChainedRateLimiter(
            [
                new RedisRateLimiter(_redis, policy, identifier, perMinute, minuteWindow, _logger),
                new RedisRateLimiter(_redis, policy, identifier, perDay, dayWindow, _logger)
            ]);
        }

        return new ChainedRateLimiter(
        [
            new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
            {
                PermitLimit = perMinute,
                Window = minuteWindow,
                QueueLimit = 0,
                AutoReplenishment = true
            }),
            new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
            {
                PermitLimit = perDay,
                Window = dayWindow,
                QueueLimit = 0,
                AutoReplenishment = true
            })
        ]);
    }
}
