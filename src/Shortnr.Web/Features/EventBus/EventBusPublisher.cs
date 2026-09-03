using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Shortnr.Web.Features.EventBus;

/// <summary>
/// Publishes shortnr domain events to the distributed event bus when <c>EventBus:Provider=RabbitMQ</c>.
/// When the provider is <see cref="EventBusProvider.InProcess"/> (the zero-config default) every
/// call is a no-op, leaving PRD-011's webhook <c>Channel&lt;T&gt;</c> and PRD-006's click-stream
/// <c>Channel&lt;T&gt;</c> as the sole handlers. Publishing never throws: connection/broker
/// failures are caught and logged, and the in-process bus has already processed the event
/// (PRD-018 Requirement 4 — graceful degradation).
/// </summary>
public sealed class EventBusPublisher
{
    private readonly EventBusProvider _provider;
    private readonly RabbitMqConnectionProvider _rabbitMq;
    private readonly ILogger<EventBusPublisher> _logger;

    public EventBusPublisher(
        IConfiguration configuration,
        RabbitMqConnectionProvider rabbitMq,
        ILogger<EventBusPublisher> logger)
    {
        _provider = EventBusProviderHelper.ResolveProvider(configuration);
        _rabbitMq = rabbitMq;
        _logger = logger;
    }

    /// <summary>
    /// Publishes <paramref name="data"/> to the event exchange under <paramref name="routingKey"/>.
    /// The payload is wrapped in a <see cref="DistributedEvent"/> envelope. No-op when the
    /// in-process provider is selected; swallowed-and-logged when RabbitMQ is unreachable.
    /// </summary>
    public Task PublishAsync(string routingKey, object data, CancellationToken cancellationToken = default)
    {
        if (_provider != EventBusProvider.RabbitMQ)
            return Task.CompletedTask;

        try
        {
            var envelope = new DistributedEvent(routingKey, DateTime.UtcNow, data);
            var body = JsonSerializer.SerializeToUtf8Bytes(envelope);
            _rabbitMq.Publish(routingKey, body);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to publish {RoutingKey} to RabbitMQ; in-process handling continues.", routingKey);
        }

        return Task.CompletedTask;
    }
}
