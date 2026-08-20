using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Shortnr.Web.Features.Infrastructure;

/// <summary>
/// Reports Redis connectivity for the optional distributed rate-limit store. Registered (and
/// surfaced at <c>/health/redis</c>) only when <c>RateLimiting:Provider=Redis</c>, so k8s
/// liveness probes can distinguish "app is up, Redis is down" from "app is down". Mirrors
/// the graceful-degradation posture: a downed Redis reports <c>Unhealthy</c> but never takes
/// the app down with it.
/// </summary>
public sealed class RedisHealthCheck : IHealthCheck
{
    private readonly RedisConnectionProvider _redis;

    public RedisHealthCheck(RedisConnectionProvider redis) => _redis = redis;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!_redis.IsConfigured)
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Redis is not configured. Set 'RateLimiting:Redis:ConnectionString' or switch 'RateLimiting:Provider' back to 'InProcess'."));

        if (!_redis.TryPing(out var error))
            return Task.FromResult(HealthCheckResult.Unhealthy($"Redis unreachable: {error}"));

        return Task.FromResult(HealthCheckResult.Healthy("Redis reachable."));
    }
}
