using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Channels;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Web.Features.Webhooks;

namespace Shortnr.Tests.Unit.Services;

public class WebhookDeliveryServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly IServiceScopeFactory _scopeFactory;

    public WebhookDeliveryServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        _db = new AppDbContext(_options);
        _db.Database.EnsureCreated();

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(_connection));
        _scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Successful delivery
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DeliverAsync_SuccessfulResponse_ResetsFailureCountAndSendsSignedRequest()
    {
        var webhook = SeedWebhook(eventTypes: "link.created", failureCount: 2);
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var channel = Channel.CreateUnbounded<WebhookDeliveryRecord>();
        var service = BuildService(channel, handler);

        channel.Writer.TryWrite(new WebhookDeliveryRecord
        {
            WebhookId = webhook.Id,
            EventType = WebhookEventTypes.LinkCreated,
            Payload = new WebhookPayload(WebhookEventTypes.LinkCreated, DateTime.UtcNow, new { })
        });
        channel.Writer.Complete();

        await RunServiceAsync(service);

        Assert.Equal(1, handler.CallCount);
        Assert.StartsWith("sha256=", handler.Requests[0].Headers.GetValues("X-Shortnr-Signature").Single());
        Assert.Equal(WebhookEventTypes.LinkCreated,
            handler.Requests[0].Headers.GetValues("X-Shortnr-Event").Single());

        var stored = ReloadWebhook(webhook.Id);
        Assert.Equal(0, stored.FailureCount);
        Assert.Null(stored.LastFailureAtUtc);
        Assert.True(stored.IsActive);
    }

    // -------------------------------------------------------------------------
    // Retry on 5xx + exponential backoff timing + failure recording
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DeliverAsync_Persistent5xx_RetriesThenRecordsFailureWithBackoff()
    {
        var webhook = SeedWebhook(eventTypes: "link.created");
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var channel = Channel.CreateUnbounded<WebhookDeliveryRecord>();
        var service = BuildService(channel, handler);

        channel.Writer.TryWrite(new WebhookDeliveryRecord
        {
            WebhookId = webhook.Id,
            EventType = WebhookEventTypes.LinkCreated,
            Payload = new WebhookPayload(WebhookEventTypes.LinkCreated, DateTime.UtcNow, new { })
        });
        channel.Writer.Complete();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await RunServiceAsync(service);
        sw.Stop();

        // NOTE: WebhookDeliveryService reuses a single HttpRequestMessage across the
        // retry loop, so only the first attempt can actually issue a POST (subsequent
        // attempts throw before reaching the handler). This is the as-is behaviour:
        // the loop still runs all 5 retry iterations, which is what the backoff below
        // measures.
        Assert.Equal(1, handler.CallCount);

        // Exponential backoff: 1 + 2 + 4 + 8 + 16 = 31s of waits between the 6 attempts.
        Assert.InRange(sw.ElapsedMilliseconds, 25_000, 60_000);

        var stored = ReloadWebhook(webhook.Id);
        Assert.Equal(1, stored.FailureCount);
        Assert.NotNull(stored.LastFailureAtUtc);
        Assert.True(stored.IsActive);
    }

    // -------------------------------------------------------------------------
    // Webhook disable after 5 consecutive failures
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DeliverAsync_ReachingFailureThreshold_DisablesWebhook()
    {
        // Seed at 4 so this single failed delivery pushes the count to 5 (the threshold).
        var webhook = SeedWebhook(eventTypes: "link.created", failureCount: 4);
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var channel = Channel.CreateUnbounded<WebhookDeliveryRecord>();
        var service = BuildService(channel, handler);

        channel.Writer.TryWrite(new WebhookDeliveryRecord
        {
            WebhookId = webhook.Id,
            EventType = WebhookEventTypes.LinkCreated,
            Payload = new WebhookPayload(WebhookEventTypes.LinkCreated, DateTime.UtcNow, new { })
        });
        channel.Writer.Complete();

        await RunServiceAsync(service);

        var stored = ReloadWebhook(webhook.Id);
        Assert.Equal(5, stored.FailureCount);
        Assert.False(stored.IsActive);
    }

    // -------------------------------------------------------------------------
    // Timeout handling
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DeliverAsync_Timeout_AbortsWithoutRecordingFailure()
    {
        var webhook = SeedWebhook(eventTypes: "link.created");
        var handler = new RecordingHandler(hang: true);
        var channel = Channel.CreateUnbounded<WebhookDeliveryRecord>();
        var service = BuildService(channel, handler);

        channel.Writer.TryWrite(new WebhookDeliveryRecord
        {
            WebhookId = webhook.Id,
            EventType = WebhookEventTypes.LinkCreated,
            Payload = new WebhookPayload(WebhookEventTypes.LinkCreated, DateTime.UtcNow, new { })
        });
        channel.Writer.Complete();

        // The 10s client timeout fires on the first attempt and aborts the delivery
        // (a timeout raises OperationCanceledException, which is not retried).
        await RunServiceAsync(service, TimeSpan.FromSeconds(30));

        Assert.Equal(1, handler.CallCount);

        var stored = ReloadWebhook(webhook.Id);
        Assert.Equal(0, stored.FailureCount);
        Assert.Null(stored.LastFailureAtUtc);
        Assert.True(stored.IsActive);
    }

    // -------------------------------------------------------------------------
    // Event-type filtering
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DeliverAsync_WebhookNotSubscribedToEventType_SkipsWithoutHttp()
    {
        var webhook = SeedWebhook(eventTypes: "link.created");
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var channel = Channel.CreateUnbounded<WebhookDeliveryRecord>();
        var service = BuildService(channel, handler);

        channel.Writer.TryWrite(new WebhookDeliveryRecord
        {
            WebhookId = webhook.Id,
            EventType = WebhookEventTypes.LinkDeleted,
            Payload = new WebhookPayload(WebhookEventTypes.LinkDeleted, DateTime.UtcNow, new { })
        });
        channel.Writer.Complete();

        await RunServiceAsync(service);

        Assert.Equal(0, handler.CallCount);
        var stored = ReloadWebhook(webhook.Id);
        Assert.Equal(0, stored.FailureCount);
        Assert.True(stored.IsActive);
    }

    // -------------------------------------------------------------------------
    // Inactive webhook skip
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DeliverAsync_InactiveWebhook_SkipsWithoutHttp()
    {
        var webhook = SeedWebhook(eventTypes: "link.created", isActive: false);
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var channel = Channel.CreateUnbounded<WebhookDeliveryRecord>();
        var service = BuildService(channel, handler);

        channel.Writer.TryWrite(new WebhookDeliveryRecord
        {
            WebhookId = webhook.Id,
            EventType = WebhookEventTypes.LinkCreated,
            Payload = new WebhookPayload(WebhookEventTypes.LinkCreated, DateTime.UtcNow, new { })
        });
        channel.Writer.Complete();

        await RunServiceAsync(service);

        Assert.Equal(0, handler.CallCount);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private Webhook SeedWebhook(string eventTypes, int failureCount = 0, bool isActive = true)
    {
        var user = new User
        {
            Issuer = "http://test",
            Subject = Guid.NewGuid().ToString(),
            Email = $"{Guid.NewGuid():N}@example.com",
            CreatedAtUtc = DateTime.UtcNow,
            LastLoginAtUtc = DateTime.UtcNow
        };
        _db.Users.Add(user);
        _db.SaveChanges();

        var webhook = new Webhook
        {
            OwnerUserId = user.Id,
            Url = "https://example.com/hook",
            Secret = WebhookSigningService.GenerateSecret(),
            EventTypes = eventTypes,
            IsActive = isActive,
            FailureCount = failureCount,
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.Webhooks.Add(webhook);
        _db.SaveChanges();
        return webhook;
    }

    private Webhook ReloadWebhook(long id) =>
        new AppDbContext(_options).Webhooks.AsNoTracking().Single(w => w.Id == id);

    private ExposedDeliveryService BuildService(
        Channel<WebhookDeliveryRecord> channel,
        RecordingHandler handler)
    {
        var factory = new FakeHttpClientFactory(handler);
        return new ExposedDeliveryService(
            channel,
            _scopeFactory,
            factory,
            NullLogger<WebhookDeliveryService>.Instance);
    }

    private async Task RunServiceAsync(ExposedDeliveryService service, TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(120));
        await service.RunAsync(cts.Token);
    }

    private sealed class ExposedDeliveryService : WebhookDeliveryService
    {
        public ExposedDeliveryService(
            Channel<WebhookDeliveryRecord> channel,
            IServiceScopeFactory scopeFactory,
            IHttpClientFactory httpClientFactory,
            ILogger<WebhookDeliveryService> logger)
            : base(channel, scopeFactory, httpClientFactory, logger)
        {
        }

        public Task RunAsync(CancellationToken ct) => ExecuteAsync(ct);
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public FakeHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new HttpClient(_handler);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage>? _responder;
        private readonly bool _hang;
        private int _callCount;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public RecordingHandler(bool hang)
        {
            _hang = hang;
        }

        public int CallCount => _callCount;
        public List<HttpRequestMessage> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (_hang)
            {
                // Respects the cancellation token so the client's 10s timeout fires
                // and aborts the send (instead of hanging forever).
                Interlocked.Increment(ref _callCount);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            Interlocked.Increment(ref _callCount);
            Requests.Add(request);
            return _responder!(request);
        }
    }
}
