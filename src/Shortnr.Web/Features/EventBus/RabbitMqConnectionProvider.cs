using System.Text;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Shortnr.Web.Features.EventBus;

/// <summary>
/// Owns the <see cref="IConnection"/> for the optional RabbitMQ-backed distributed event bus.
/// Registered unconditionally (even when <c>EventBus:Provider=InProcess</c>, where it is never
/// touched), so <see cref="EventBusPublisher"/> can hold it without provider-conditional DI.
/// Connecting is lazy and never blocks startup: the connection is established on first publish,
/// with automatic recovery enabled so a transient broker outage re-establishes in the
/// background. A downed RabbitMQ degrades to the in-process event bus instead of 500s
/// (PRD-018 Requirement 4). It is <b>not</b> a general-purpose message broker — it only ever
/// publishes shortnr's domain events (PRD-018 Non-goal).
/// </summary>
public sealed class RabbitMqConnectionProvider : IDisposable
{
    /// <summary>Default topic exchange name, per PRD-018 Requirement 1.</summary>
    public const string DefaultExchange = "shortnr.events";

    private readonly EventBusOptions _options;
    private readonly ILogger<RabbitMqConnectionProvider> _logger;
    private readonly Lazy<IConnection> _connection;
    private int _warningLogged;

    public RabbitMqConnectionProvider(IOptions<EventBusOptions> options, ILogger<RabbitMqConnectionProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
        // ExecutionAndPublication: if Connect() throws (bad endpoint/credentials) the exception
        // is not cached, so a later publish retries rather than being permanently wedged.
        _connection = new Lazy<IConnection>(Connect, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>True when a connection string was supplied (provider opted in).</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.RabbitMq.ConnectionString);

    /// <summary>The durable topic exchange shortnr publishes to.</summary>
    public string Exchange =>
        string.IsNullOrWhiteSpace(_options.RabbitMq.Exchange) ? DefaultExchange : _options.RabbitMq.Exchange;

    /// <summary>
    /// Best-effort channel access used by the publisher's fast-degradation path: returns
    /// <c>false</c> (with the exchange declared when reachable) instead of throwing when RabbitMQ
    /// is down or was never reachable, so the caller can fall back to the in-process bus.
    /// </summary>
    public bool TryGetChannel(out IModel? channel)
    {
        channel = null;
        if (!IsConfigured)
            return false;

        try
        {
            var conn = _connection.Value;
            if (!conn.IsOpen)
                return false;

            channel = conn.CreateModel();
            DeclareExchange(channel);
            return true;
        }
        catch (Exception ex)
        {
            LogWarning(ex);
            return false;
        }
    }

    /// <summary>
    /// Publishes <paramref name="body"/> to <see cref="Exchange"/> with <paramref name="routingKey"/>
    /// as the routing key. Messages are persistent (<see cref="IBasicProperties.DeliveryMode"/> = 2)
    /// and use publisher confirms (PRD-018 Requirement 3, at-least-once). Failures are swallowed
    /// and logged so the calling request path never crashes — the in-process bus already handled
    /// the event locally.
    /// </summary>
    public void Publish(string routingKey, ReadOnlyMemory<byte> body)
    {
        if (!TryGetChannel(out var channel) || channel is null)
            return;

        try
        {
            // Publisher confirms give broker acknowledgement of the publish — the "explicit ack"
            // half of at-least-once. Consumers must still be idempotent (PRD-018 Risks).
            channel.ConfirmSelect();

            var properties = channel.CreateBasicProperties();
            properties.DeliveryMode = 2; // Persistent
            properties.ContentType = "application/json";
            properties.Persistent = true;

            channel.BasicPublish(Exchange, routingKey, false, properties, body);
            // Publisher confirms give broker acknowledgement of the publish — the "explicit ack"
            // half of at-least-once. A false return means the broker nacked the message.
            if (!channel.WaitForConfirms())
                LogWarning(new InvalidOperationException(
                    $"RabbitMQ did not confirm publish of '{routingKey}'. Event may need redelivery."));
        }
        catch (Exception ex)
        {
            LogWarning(ex);
        }
        finally
        {
            try
            {
                if (channel.IsOpen)
                    channel.Close();
                channel.Dispose();
            }
            catch
            {
                // best-effort cleanup; nothing to do if the channel is already gone
            }
        }
    }

    /// <summary>
    /// Connectivity probe for the RabbitMQ health check. Returns <c>true</c> when the connection
    /// is open and the exchange can be declared, otherwise <c>false</c> with the error message.
    /// </summary>
    public bool TryPing(out string? error)
    {
        error = null;
        if (!IsConfigured)
            return false;

        try
        {
            var conn = _connection.Value;
            if (!conn.IsOpen)
            {
                error = "RabbitMQ connection is not open.";
                return false;
            }

            using var channel = conn.CreateModel();
            DeclareExchange(channel);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private void DeclareExchange(IModel channel) =>
        // Durable + topic so messages survive broker restarts and consumers can bind by routing
        // key (link.clicked, #, etc.). Idempotent: re-declaring with identical args is a no-op.
        channel.ExchangeDeclare(Exchange, ExchangeType.Topic, durable: true, autoDelete: false);

    private IConnection Connect()
    {
        var factory = new ConnectionFactory
        {
            Uri = new Uri(_options.RabbitMq.ConnectionString, UriKind.Absolute),
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
            RequestedConnectionTimeout = TimeSpan.FromSeconds(10),
            SocketReadTimeout = TimeSpan.FromSeconds(10),
            SocketWriteTimeout = TimeSpan.FromSeconds(10)
        };

        var connection = factory.CreateConnection();
        connection.ConnectionShutdown += (_, e) =>
            _logger.LogWarning("RabbitMQ connection shutdown ({ReplyText}). Event publishing falls back to in-process until reconnected.", e.ReplyText);
        connection.CallbackException += (_, e) =>
            _logger.LogWarning(e.Exception, "RabbitMQ connection callback error. Event publishing falls back to in-process.");
        return connection;
    }

    private void LogWarning(Exception ex)
    {
        if (Interlocked.Exchange(ref _warningLogged, 1) == 1)
            return;
        _logger.LogWarning(ex,
            "RabbitMQ event bus is unavailable (connection string '{ConnectionString}'). " +
            "Events fall back to in-process handling until the broker is reachable.",
            string.IsNullOrEmpty(_options.RabbitMq.ConnectionString) ? "<empty>" : MaskConnectionString(_options.RabbitMq.ConnectionString));
    }

    /// <summary>Redacts credentials in the connection string before logging it.</summary>
    private static string MaskConnectionString(string connectionString)
    {
        try
        {
            var uri = new Uri(connectionString, UriKind.Absolute);
            var userInfo = uri.UserInfo;
            return userInfo.Length == 0 ? connectionString : connectionString.Replace(userInfo, "***:***");
        }
        catch
        {
            // Not a URI (e.g. host:port form) — leave it; it has no credentials anyway.
            return connectionString;
        }
    }

    public void Dispose()
    {
        // Only touch the connection if it was actually created — forcing Value on an InProcess
        // setup would attempt a (pointless) connection to an empty string.
        if (_connection.IsValueCreated)
        {
            try { _connection.Value.Dispose(); }
            catch { /* best-effort */ }
        }
    }
}
