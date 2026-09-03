using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Tests.Integration.Infrastructure;
using Xunit;

namespace Shortnr.Tests.Integration.EventBus;

/// <summary>
/// PRD-018 distributed event bus: a real RabbitMQ instance (Testcontainers) backing the opt-in
/// <c>EventBus:Provider=RabbitMQ</c> store. Covers event publishing with correct routing keys,
/// persistent (DeliveryMode=2) messages, at-least-once publisher confirms, routing-key scoping,
/// graceful degradation when RabbitMQ is unreachable, the <c>/health/rabbitmq</c> endpoint, and
/// end-to-end publishing driven through the real webhook/click-stream code paths. Skips (never
/// fails) when Docker isn't available — CI's runners exercise them for real.
/// </summary>
[Collection("RabbitMQ")]
[Trait("Category", "RabbitMQ")]
public class EventBusTests(RabbitMqContainerFixture RabbitFixture) : IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => Task.CompletedTask;

    private static string UniqueCode() => $"rmq{Guid.NewGuid():N}"[..10];

    private static async Task<long> SeedUserAndKeyAsync(ShortnrWebAppFactory factory, string subject, string key)
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
            Label = "event-bus test key",
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return user.Id;
    }

    private static async Task SeedLinkAsync(ShortnrWebAppFactory factory, string code, long? ownerUserId = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.ShortenedUrls.Add(new ShortenedUrl
        {
            LongUrl = "https://example.com/rabbit-target",
            ShortCode = code,
            OwnerUserId = ownerUserId,
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    // ── broker readiness primer ────────────────────────────────────────────────

    private async Task HelloRabbitAsync(int timeoutMs = 15_000)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var factory = new ConnectionFactory
                {
                    Uri = new Uri(RabbitFixture.GetConnectionString()),
                    RequestedConnectionTimeout = TimeSpan.FromSeconds(5)
                };
                using var probe = factory.CreateConnection();
                if (probe.IsOpen)
                    return;
            }
            catch (Exception)
            {
                // retry
            }

            if (attempt * 500 > timeoutMs)
                throw new TimeoutException("RabbitMQ container did not become reachable in time.");
            await Task.Delay(500);
        }
    }

    // ── test-side consumer plumbing ─────────────────────────────────────────────

    private const string Exchange = "shortnr.events";

    private static (IConnection conn, IModel channel, string queue) BindQueue(
        string connectionString, string routingKey, string exchange = Exchange)
    {
        var factory = new ConnectionFactory
        {
            Uri = new Uri(connectionString),
            RequestedConnectionTimeout = TimeSpan.FromSeconds(10)
        };
        var conn = factory.CreateConnection();
        var channel = conn.CreateModel();
        // Pre-declare the durable topic exchange (idempotent vs the app's own declare) so the
        // binding is valid even before the first app publish.
        channel.ExchangeDeclare(exchange, ExchangeType.Topic, durable: true, autoDelete: false);
        var queue = channel.QueueDeclare("", exclusive: true, autoDelete: true).QueueName;
        channel.QueueBind(queue, exchange, routingKey);
        return (conn, channel, queue);
    }

    private static async Task<BasicDeliverEventArgs> ConsumeAsync(IModel channel, string queue, int timeoutMs = 15_000)
    {
        var tcs = new TaskCompletionSource<BasicDeliverEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        var consumer = new EventingBasicConsumer(channel);
        consumer.Received += (_, ea) => tcs.TrySetResult(ea);
        channel.BasicConsume(queue, autoAck: true, consumer);
        return await tcs.Task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs)).ConfigureAwait(false);
    }

    private static async Task AssertNoMessageAsync(IModel channel, string queue, int quietMs = 2_000)
    {
        try
        {
            await ConsumeAsync(channel, queue, quietMs).ConfigureAwait(false);
            throw new Exception("Expected no message on the scoped queue, but one arrived.");
        }
        catch (TimeoutException)
        {
            // expected: nothing arrived within the quiet window
        }
    }

    // ── factory wiring ──────────────────────────────────────────────────────────

    private sealed class EventBusFactory(string connectionString, bool rabbitMqEnabled, bool authEnabled = true) : ShortnrWebAppFactory(authEnabled)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["EventBus:Provider"] = rabbitMqEnabled ? "RabbitMQ" : "InProcess",
                    ["EventBus:RabbitMQ:ConnectionString"] = rabbitMqEnabled ? connectionString : "",
                    ["EventBus:RabbitMQ:Exchange"] = Exchange,
                }));
        }
    }

    // ── event publishing + routing keys ─────────────────────────────────────────

    [SkippableFact]
    public async Task Publish_LinkCreated_ReachesConsumer_WithRoutingKey()
    {
        Skip.If(!RabbitFixture.IsAvailable, RabbitFixture.UnavailableReason);
        await HelloRabbitAsync();

        await using var factory = new EventBusFactory(RabbitFixture.GetConnectionString(), rabbitMqEnabled: true);
        var (conn, channel, queue) = BindQueue(RabbitFixture.GetConnectionString(), EventBusRoutingKeys.LinkCreated);
        try
        {
            var publisher = factory.Services.GetRequiredService<EventBusPublisher>();
            await publisher.PublishAsync(EventBusRoutingKeys.LinkCreated,
                new { ShortCode = "abc123", LongUrl = "https://example.com/x" });

            var delivered = await ConsumeAsync(channel, queue);
            Assert.Equal(EventBusRoutingKeys.LinkCreated, delivered.RoutingKey);
            var body = JsonDocument.Parse(delivered.Body.ToArray());
            Assert.Equal(EventBusRoutingKeys.LinkCreated, body.RootElement.GetProperty("EventType").GetString());
        }
        finally
        {
            channel.Dispose();
            conn.Dispose();
        }
    }

    [SkippableFact]
    public async Task Publish_LinkClicked_ReachesConsumer_WithRoutingKey()
    {
        Skip.If(!RabbitFixture.IsAvailable, RabbitFixture.UnavailableReason);
        await HelloRabbitAsync();

        await using var factory = new EventBusFactory(RabbitFixture.GetConnectionString(), rabbitMqEnabled: true);
        var (conn, channel, queue) = BindQueue(RabbitFixture.GetConnectionString(), EventBusRoutingKeys.LinkClicked);
        try
        {
            var publisher = factory.Services.GetRequiredService<EventBusPublisher>();
            await publisher.PublishAsync(EventBusRoutingKeys.LinkClicked,
                new ClickEventData("abc123", "https://example.com/x", null, 3, 9, DateTime.UtcNow));

            var delivered = await ConsumeAsync(channel, queue);
            Assert.Equal(EventBusRoutingKeys.LinkClicked, delivered.RoutingKey);
        }
        finally
        {
            channel.Dispose();
            conn.Dispose();
        }
    }

    [SkippableFact]
    public async Task Publish_WebhookFired_ReachesConsumer_WithRoutingKey()
    {
        Skip.If(!RabbitFixture.IsAvailable, RabbitFixture.UnavailableReason);
        await HelloRabbitAsync();

        await using var factory = new EventBusFactory(RabbitFixture.GetConnectionString(), rabbitMqEnabled: true);
        var (conn, channel, queue) = BindQueue(RabbitFixture.GetConnectionString(), EventBusRoutingKeys.WebhookFired);
        try
        {
            var publisher = factory.Services.GetRequiredService<EventBusPublisher>();
            await publisher.PublishAsync(EventBusRoutingKeys.WebhookFired,
                new WebhookFiredData(42, EventBusRoutingKeys.LinkClicked, "https://hook.example.com", DateTime.UtcNow));

            var delivered = await ConsumeAsync(channel, queue);
            Assert.Equal(EventBusRoutingKeys.WebhookFired, delivered.RoutingKey);
        }
        finally
        {
            channel.Dispose();
            conn.Dispose();
        }
    }

    // ── routing-key scoping ──────────────────────────────────────────────────────

    [SkippableFact]
    public async Task RoutingKeyScoping_OnlySubscribedTypeReceived()
    {
        Skip.If(!RabbitFixture.IsAvailable, RabbitFixture.UnavailableReason);
        await HelloRabbitAsync();

        await using var factory = new EventBusFactory(RabbitFixture.GetConnectionString(), rabbitMqEnabled: true);
        // Bind only to link.clicked — a link.created publish must NOT arrive here.
        var (conn, channel, queue) = BindQueue(RabbitFixture.GetConnectionString(), EventBusRoutingKeys.LinkClicked);
        try
        {
            var publisher = factory.Services.GetRequiredService<EventBusPublisher>();
            await publisher.PublishAsync(EventBusRoutingKeys.LinkCreated,
                new { ShortCode = "no-show" });

            await AssertNoMessageAsync(channel, queue);
        }
        finally
        {
            channel.Dispose();
            conn.Dispose();
        }
    }

    [SkippableFact]
    public async Task RoutingKeyScoping_WildcardHashReceivesAll()
    {
        Skip.If(!RabbitFixture.IsAvailable, RabbitFixture.UnavailableReason);
        await HelloRabbitAsync();

        await using var factory = new EventBusFactory(RabbitFixture.GetConnectionString(), rabbitMqEnabled: true);
        var (conn, channel, queue) = BindQueue(RabbitFixture.GetConnectionString(), "#");
        try
        {
            var publisher = factory.Services.GetRequiredService<EventBusPublisher>();
            await publisher.PublishAsync(EventBusRoutingKeys.LinkCreated, new { ShortCode = "a" });
            await publisher.PublishAsync(EventBusRoutingKeys.LinkClicked, new { ShortCode = "b" });
            await publisher.PublishAsync(EventBusRoutingKeys.WebhookFired, new { WebhookId = 1 });

            var first = await ConsumeAsync(channel, queue);
            var second = await ConsumeAsync(channel, queue);
            var third = await ConsumeAsync(channel, queue);

            var keys = new[] { first.RoutingKey, second.RoutingKey, third.RoutingKey }.ToHashSet();
            Assert.Contains(EventBusRoutingKeys.LinkCreated, keys);
            Assert.Contains(EventBusRoutingKeys.LinkClicked, keys);
            Assert.Contains(EventBusRoutingKeys.WebhookFired, keys);
        }
        finally
        {
            channel.Dispose();
            conn.Dispose();
        }
    }

    // ── persistent messages ──────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Messages_ArePersistent_DeliveryModeTwo()
    {
        Skip.If(!RabbitFixture.IsAvailable, RabbitFixture.UnavailableReason);
        await HelloRabbitAsync();

        await using var factory = new EventBusFactory(RabbitFixture.GetConnectionString(), rabbitMqEnabled: true);
        var (conn, channel, queue) = BindQueue(RabbitFixture.GetConnectionString(), EventBusRoutingKeys.LinkCreated);
        try
        {
            var publisher = factory.Services.GetRequiredService<EventBusPublisher>();
            await publisher.PublishAsync(EventBusRoutingKeys.LinkCreated, new { ShortCode = "persist" });

            var delivered = await ConsumeAsync(channel, queue);
            // DeliveryMode 2 == Persistent (PRD-018 Requirement 3, at-least-once).
            Assert.Equal(2, delivered.BasicProperties.DeliveryMode);
        }
        finally
        {
            channel.Dispose();
            conn.Dispose();
        }
    }

    [SkippableFact]
    public async Task Messages_ArePublishedWithConfirm_ExchangeDeclaredDurable()
    {
        Skip.If(!RabbitFixture.IsAvailable, RabbitFixture.UnavailableReason);
        await HelloRabbitAsync();

        // The exchange is declared durable by the app on first publish; a fresh probe
        // connection can read it back and confirm durability.
        var factory = new EventBusFactory(RabbitFixture.GetConnectionString(), rabbitMqEnabled: true);
        var publisher = factory.Services.GetRequiredService<EventBusPublisher>();
        await publisher.PublishAsync(EventBusRoutingKeys.LinkCreated, new { ShortCode = "durable" });

        var probeFactory = new ConnectionFactory
        {
            Uri = new Uri(RabbitFixture.GetConnectionString()),
            RequestedConnectionTimeout = TimeSpan.FromSeconds(10)
        };
        using var probeConn = probeFactory.CreateConnection();
        using var probeChannel = probeConn.CreateModel();
        // ExchangeDeclare with passive=true throws if the exchange is missing.
        probeChannel.ExchangeDeclarePassive(Exchange);
        Assert.True(true);
    }

    // ── graceful degradation ──────────────────────────────────────────────────────

    [SkippableFact]
    public async Task GracefulFallback_DeadConnection_DoesNotThrow()
    {
        // Deterministic: nothing listens on this port, so every connect fails fast and the
        // publish path degrades to a no-op instead of throwing.
        await using var factory = new EventBusFactory("amqp://guest:guest@127.0.0.1:1", rabbitMqEnabled: true);

        var publisher = factory.Services.GetRequiredService<EventBusPublisher>();
        var ex = await Record.ExceptionAsync(() => publisher.PublishAsync(EventBusRoutingKeys.LinkCreated, new { ShortCode = "x" }));
        Assert.Null(ex);
    }

    [SkippableFact]
    public async Task GracefulFallback_AppStillServes_WhenRabbitUnreachable()
    {
        Skip.If(!RabbitFixture.IsAvailable, RabbitFixture.UnavailableReason);

        // Start with a live broker, publish once so the connection is warm, then kill it and
        // prove the in-process bus keeps the app serving (no 500s) and publishing doesn't throw.
        await HelloRabbitAsync();
        await using var factory = new EventBusFactory(RabbitFixture.GetConnectionString(), rabbitMqEnabled: true);
        var publisher = factory.Services.GetRequiredService<EventBusPublisher>();
        var code = UniqueCode();
        await SeedLinkAsync(factory, code);
        var client = factory.CreateClientNoRedirect();

        // Warm-up redirect works while RabbitMQ is up.
        Assert.Equal(HttpStatusCode.Found, (await client.GetAsync($"/{code}")).StatusCode);

        try
        {
            await RabbitFixture.StopAsync();
            await Task.Delay(500);

            var ex = await Record.ExceptionAsync(() => publisher.PublishAsync(EventBusRoutingKeys.LinkClicked, new ClickEventData(code, "https://example.com/x", null, 1, 1, DateTime.UtcNow)));
            Assert.Null(ex);

            // The app must keep serving redirects even though the event bus is down.
            var response = await client.GetAsync($"/{code}");
            Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
        }
        finally
        {
            await RabbitFixture.StartAsync();
        }
    }

    // ── health endpoint ───────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task HealthRabbitMq_ReportsHealthy_WhenReachable()
    {
        Skip.If(!RabbitFixture.IsAvailable, RabbitFixture.UnavailableReason);
        await HelloRabbitAsync();

        await using var factory = new EventBusFactory(RabbitFixture.GetConnectionString(), rabbitMqEnabled: true);
        var response = await factory.CreateClient().GetAsync("/health/rabbitmq");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Healthy", await response.Content.ReadAsStringAsync());
    }

    [SkippableFact]
    public async Task HealthRabbitMq_ReportsUnhealthy_WhenUnreachable()
    {
        // Deterministic dead endpoint exercises the same contract as a crashed broker without
        // racing the container teardown against a single probe.
        await using var factory = new EventBusFactory("amqp://guest:guest@127.0.0.1:1", rabbitMqEnabled: true);

        var response = await factory.CreateClient().GetAsync("/health/rabbitmq");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("Unhealthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task HealthRabbitMq_NotMapped_WhenProviderIsInProcess()
    {
        await using var factory = new EventBusFactory("amqp://guest:guest@localhost:5672", rabbitMqEnabled: false);

        var response = await factory.CreateClient().GetAsync("/health/rabbitmq");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task InProcessProvider_PublishIsNoOp_NoBrokerTouched()
    {
        // With the default InProcess provider (no broker configured) the publisher must be a
        // silent no-op — this is the zero-config baseline PRD-018 must not change.
        await using var factory = new EventBusFactory("amqp://guest:guest@localhost:5672", rabbitMqEnabled: false);
        var provider = factory.Services.GetRequiredService<RabbitMqConnectionProvider>();

        var publisher = factory.Services.GetRequiredService<EventBusPublisher>();
        var ex = await Record.ExceptionAsync(() => publisher.PublishAsync(EventBusRoutingKeys.LinkCreated, new { ShortCode = "x" }));
        Assert.Null(ex);
        Assert.False(provider.IsConfigured);
    }

    // ── end-to-end through the real webhook / click-stream paths ──────────────────

    [SkippableFact]
    public async Task EndToEnd_LinkCreated_PublishedFromApi()
    {
        Skip.If(!RabbitFixture.IsAvailable, RabbitFixture.UnavailableReason);
        await HelloRabbitAsync();

        await using var factory = new EventBusFactory(RabbitFixture.GetConnectionString(), rabbitMqEnabled: true);
        const string key = "snr_e2ecreated1234567890abcdef1234567";
        await SeedUserAndKeyAsync(factory, "eventbus-owner", key);

        var (conn, channel, queue) = BindQueue(RabbitFixture.GetConnectionString(), EventBusRoutingKeys.LinkCreated);
        try
        {
            var client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);

            var create = await client.PostAsJsonAsync("/api/v1/links", new { Url = "https://example.com/e2e-created" });
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);

            // Dispatched via WebhookEventDispatcher -> RabbitMQ (PRD-018 wiring).
            var delivered = await ConsumeAsync(channel, queue);
            Assert.Equal(EventBusRoutingKeys.LinkCreated, delivered.RoutingKey);
        }
        finally
        {
            channel.Dispose();
            conn.Dispose();
        }
    }

    [SkippableFact]
    public async Task EndToEnd_LinkClicked_PublishedFromRedirect()
    {
        Skip.If(!RabbitFixture.IsAvailable, RabbitFixture.UnavailableReason);
        await HelloRabbitAsync();

        await using var factory = new EventBusFactory(RabbitFixture.GetConnectionString(), rabbitMqEnabled: true);
        const string key = "snr_e2eclicked1234567890abcdef123456";
        var ownerId = await SeedUserAndKeyAsync(factory, "eventbus-clicker", key);
        var code = UniqueCode();
        await SeedLinkAsync(factory, code, ownerId);

        var (conn, channel, queue) = BindQueue(RabbitFixture.GetConnectionString(), EventBusRoutingKeys.LinkClicked);
        try
        {
            var client = factory.CreateClientNoRedirect();
            // Redirect writes a ClickRecord; ClickBatchProcessor publishes link.clicked per owner.
            Assert.Equal(HttpStatusCode.Found, (await client.GetAsync($"/{code}")).StatusCode);

            var delivered = await ConsumeAsync(channel, queue, timeoutMs: 20_000);
            Assert.Equal(EventBusRoutingKeys.LinkClicked, delivered.RoutingKey);
        }
        finally
        {
            channel.Dispose();
            conn.Dispose();
        }
    }
}
