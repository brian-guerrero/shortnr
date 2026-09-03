namespace Shortnr.Web.Features.EventBus;

/// <summary>
/// Envelope published to the RabbitMQ exchange for every domain event. Consumers can read
/// <see cref="EventType"/> to dispatch and <see cref="Data"/> for the payload (the PRD-011
/// <c>WebhookPayload</c> for link events, or an event-specific DTO below). The envelope keeps
/// shortnr's event shape stable independent of any single webhook version.
/// </summary>
public sealed record DistributedEvent(
    string EventType,
    DateTime OccurredAtUtc,
    object? Data);

/// <summary>
/// Payload for a <c>link.clicked</c> event. Mirrors the salient fields of the PRD-011
/// <c>WebhookClickData</c> minus the webhook-specific window bounds — click-stream consumers
/// (PRD-006 analytics, n8n flows, ...) only need the delta + total.
/// </summary>
public sealed record ClickEventData(
    string ShortCode,
    string LongUrl,
    string? Domain,
    int ClickDelta,
    long TotalClicks,
    DateTime OccurredAtUtc);

/// <summary>
/// Payload for a <c>webhook.fired</c> event, emitted when a PRD-011 webhook delivery succeeds.
/// </summary>
public sealed record WebhookFiredData(
    long WebhookId,
    string EventType,
    string Url,
    DateTime DeliveredAtUtc);
