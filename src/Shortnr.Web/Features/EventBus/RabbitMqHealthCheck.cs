using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Shortnr.Web.Features.EventBus;

/// <summary>
/// Reports RabbitMQ connectivity for the optional distributed event bus. Registered (and
/// surfaced at <c>/health/rabbitmq</c>) only when <c>EventBus:Provider=RabbitMQ</c>, so k8s
/// liveness probes can distinguish "app is up, RabbitMQ is down" from "app is down". Mirrors
/// the graceful-degradation posture: a downed RabbitMQ reports <c>Unhealthy</c> but never takes
/// the app down with it (PRD-018 Requirement 5).
/// </summary>
public sealed class RabbitMqHealthCheck : IHealthCheck
{
    private readonly RabbitMqConnectionProvider _rabbitMq;

    public RabbitMqHealthCheck(RabbitMqConnectionProvider rabbitMq) => _rabbitMq = rabbitMq;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!_rabbitMq.IsConfigured)
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "RabbitMQ is not configured. Set 'EventBus:RabbitMQ:ConnectionString' or switch 'EventBus:Provider' back to 'InProcess'."));

        if (!_rabbitMq.TryPing(out var error))
            return Task.FromResult(HealthCheckResult.Unhealthy($"RabbitMQ unreachable: {error}"));

        return Task.FromResult(HealthCheckResult.Healthy("RabbitMQ reachable."));
    }
}
