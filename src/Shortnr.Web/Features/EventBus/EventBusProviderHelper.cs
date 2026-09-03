using Microsoft.Extensions.Configuration;

namespace Shortnr.Web.Features.EventBus;

/// <summary>
/// Resolves <c>EventBus:Provider</c> (<c>InProcess</c> | <c>RabbitMQ</c>). When the value is
/// omitted, defaults to <see cref="EventBusProvider.InProcess"/> so the zero-config in-process
/// event bus (PRD-011 <c>Channel&lt;T&gt;</c> webhooks + PRD-006 click-stream) stays the baseline.
/// Unsupported values fail fast at startup — mirroring <c>DatabaseProviderHelper.ResolveProvider</c>
/// and <c>RateLimitProviderHelper.ResolveProvider</c> so a typo surfaces immediately instead of
/// silently degrading the intended configuration.
/// </summary>
public static class EventBusProviderHelper
{
    public const string ConfigSection = "EventBus";
    public const string ProviderKey = "Provider";

    public static EventBusProvider ResolveProvider(IConfiguration configuration)
    {
        var value = configuration[$"{ConfigSection}:{ProviderKey}"];

        if (string.IsNullOrWhiteSpace(value))
            return EventBusProvider.InProcess;

        return value.Trim().ToLowerInvariant() switch
        {
            "inprocess" or "in-process" => EventBusProvider.InProcess,
            "rabbitmq" => EventBusProvider.RabbitMQ,
            _ => throw new InvalidOperationException(
                $"Unsupported '{ConfigSection}:{ProviderKey}' value '{value}'. " +
                "Supported values: InProcess, RabbitMQ.")
        };
    }
}

/// <summary>
/// The in-process <see cref="System.Threading.Channels.Channel{T}"/> + <see cref="BackgroundService"/>
/// event bus is the zero-config default. <see cref="EventBusProvider.RabbitMQ"/> opts in to the
/// distributed AMQP 0.9.1 event bus (PRD-018) so external consumers (Home Assistant, n8n,
/// Node-RED, custom pipelines) can subscribe to shortnr's events.
/// </summary>
public enum EventBusProvider
{
    InProcess,
    RabbitMQ
}

/// <summary>
/// AMQP routing keys published to the event exchange. They mirror the PRD-011 webhook event
/// types plus <c>webhook.fired</c> (emitted when a webhook delivery succeeds), so a consumer
/// can bind a queue to <c>link.clicked</c>, <c>link.created</c>, <c>link.deleted</c>, or
/// <c>webhook.fired</c> and receive exactly the events it cares about.
/// </summary>
public static class EventBusRoutingKeys
{
    public const string LinkCreated = "link.created";
    public const string LinkClicked = "link.clicked";
    public const string LinkDeleted = "link.deleted";
    public const string WebhookFired = "webhook.fired";
}

/// <summary>
/// Bound from the <c>EventBus</c> config section. Controls the optional distributed event bus.
/// The <c>Provider</c> value is resolved by <see cref="EventBusProviderHelper"/>; this type only
/// carries the RabbitMQ connection details (mirrors <c>RateLimitingOptions</c>, which holds only
/// the Redis sub-options and lets a separate helper own provider resolution).
/// </summary>
public class EventBusOptions
{
    public RabbitMqOptions RabbitMq { get; set; } = new();
}

/// <summary>
/// Connection settings for the optional RabbitMQ-backed distributed event bus.
/// </summary>
public class RabbitMqOptions
{
    /// <summary>
    /// AMQP connection string, e.g. <c>amqp://guest:guest@localhost:5672</c>. Empty means
    /// "not configured" (the event bus falls back to in-process).
    /// </summary>
    public string ConnectionString { get; set; } = "";

    /// <summary>
    /// Topic exchange shortnr publishes to. Defaults to <c>shortnr.events</c>. Declared
    /// durable so messages survive a broker restart (PRD-018 Requirement 3).
    /// </summary>
    public string Exchange { get; set; } = RabbitMqConnectionProvider.DefaultExchange;
}
