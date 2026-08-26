using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Web.Features.Webhooks;

namespace Shortnr.Tests.Unit.Services;

public class ClickBatchProcessorTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly Channel<ClickRecord> _channel;
    private readonly Channel<object> _sseChannel;
    private readonly ServiceProvider _serviceProvider;
    private readonly IServiceScopeFactory _scopeFactory;

    public ClickBatchProcessorTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA foreign_keys = OFF;";
            cmd.ExecuteNonQuery();
        }

        _channel = Channel.CreateUnbounded<ClickRecord>();
        _sseChannel = Channel.CreateUnbounded<object>();

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(_ => _.UseSqlite(_connection));
        _serviceProvider = services.BuildServiceProvider();
        _scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        _db.Dispose();
        _connection.Dispose();
    }

    private static ShortenedUrl Link(long id = 1, long clickCount = 0) => new()
    {
        Id = id,
        LongUrl = "https://example.com/landing",
        ShortCode = "abc" + id,
        ClickCount = clickCount,
        CreatedAtUtc = DateTime.UtcNow,
        OwnerUserId = 1
    };

    private static ClickRecord Record(long urlId = 1, string ip = "1.2.3.4",
        string ua = "Mozilla/5.0", string referer = "https://example.com") => new()
    {
        ShortenedUrlId = urlId,
        IpAddress = ip,
        UserAgent = ua,
        Referer = referer
    };

    private ClickBatchProcessor CreateProcessor(Channel<WebhookDeliveryRecord>? webhookChannel = null)
    {
        var deliveryChannel = webhookChannel ?? Channel.CreateUnbounded<WebhookDeliveryRecord>();
        var dispatcher = new WebhookEventDispatcher(deliveryChannel, _scopeFactory);
        return new ClickBatchProcessor(
            _channel,
            _scopeFactory,
            NullLogger<ClickBatchProcessor>.Instance,
            new GeoIpService(Path.Combine(Path.GetTempPath(), "nonexistent.mmdb"), NullLogger<GeoIpService>.Instance),
            _sseChannel,
            dispatcher);
    }

    private async Task ExecuteProcessorAsync(ClickBatchProcessor processor,
        IEnumerable<ClickRecord> records, int delayMs = 200)
    {
        using var cts = new CancellationTokenSource();
        var task = processor.StartAsync(cts.Token);

        foreach (var r in records)
            await _channel.Writer.WriteAsync(r);

        while (_channel.Reader.TryRead(out _)) { }

        await Task.Delay(delayMs);
        cts.Cancel();
        await task;
    }

    [Fact]
    public async Task Flush_At100Records_BatchesAndInsertsAllClickEvents()
    {
        _db.ShortenedUrls.Add(Link());
        await _db.SaveChangesAsync();

        var records = Enumerable.Range(1, 100).Select(i => Record()).ToList();
        var processor = CreateProcessor();

        await ExecuteProcessorAsync(processor, records);

        var clicks = await _db.ClickEvents.ToListAsync();
        Assert.Equal(100, clicks.Count);
        var link = await _db.ShortenedUrls.FindAsync(1L);
        Assert.Equal(100, link!.ClickCount);
    }

    [Fact]
    public async Task Flush_SingleRecord_InsertsAndUpdatesCount()
    {
        _db.ShortenedUrls.Add(Link());
        await _db.SaveChangesAsync();

        var processor = CreateProcessor();
        await ExecuteProcessorAsync(processor, [Record()]);

        var clicks = await _db.ClickEvents.ToListAsync();
        Assert.Single(clicks);
        Assert.Equal("1.2.3.4", clicks[0].IpAddress);
        var link = await _db.ShortenedUrls.FindAsync(1L);
        Assert.Equal(1, link!.ClickCount);
    }

    [Fact]
    public async Task Shutdown_StoppingTokenCancellation_ProcessorExitsCleanly()
    {
        _db.ShortenedUrls.Add(Link());
        await _db.SaveChangesAsync();

        var processor = CreateProcessor();
        using var cts = new CancellationTokenSource();
        var task = processor.StartAsync(cts.Token);

        await _channel.Writer.WriteAsync(Record());
        while (_channel.Reader.TryRead(out _)) { }
        await Task.Delay(200);

        cts.Cancel();
        await task;

        Assert.True(task.IsCompleted);
        Assert.Equal(TaskStatus.RanToCompletion, task.Status);
    }

    [Fact]
    public async Task GeoIp_InvalidIp_NullGeoFields()
    {
        _db.ShortenedUrls.Add(Link());
        await _db.SaveChangesAsync();

        var processor = CreateProcessor();
        await ExecuteProcessorAsync(processor, [Record(ip: "not-an-ip")]);

        var click = await _db.ClickEvents.SingleAsync();
        Assert.Null(click.CountryCode);
        Assert.Null(click.CountryName);
        Assert.Null(click.CityName);
        Assert.Null(click.Latitude);
        Assert.Null(click.Longitude);
    }

    [Fact]
    public async Task GeoIp_ValidIpButNoDatabase_NullGeoFields()
    {
        _db.ShortenedUrls.Add(Link());
        await _db.SaveChangesAsync();

        var processor = CreateProcessor();
        await ExecuteProcessorAsync(processor, [Record(ip: "8.8.8.8")]);

        var click = await _db.ClickEvents.SingleAsync();
        Assert.Null(click.CountryCode);
        Assert.Null(click.CityName);
    }

    [Fact]
    public async Task ClickCount_MultipleClicksSameLink_AccumulatesCorrectly()
    {
        _db.ShortenedUrls.Add(Link(clickCount: 5));
        await _db.SaveChangesAsync();

        var records = Enumerable.Range(1, 10).Select(_ => Record()).ToList();
        var processor = CreateProcessor();
        await ExecuteProcessorAsync(processor, records);

        var link = await _db.ShortenedUrls.FindAsync(1L);
        Assert.Equal(15, link!.ClickCount);
    }

    [Fact]
    public async Task ClickCount_MultipleLinksEachClick_UpdatesCorrectly()
    {
        _db.ShortenedUrls.Add(Link(id: 1, clickCount: 0));
        _db.ShortenedUrls.Add(Link(id: 2, clickCount: 3));
        await _db.SaveChangesAsync();

        var records = new List<ClickRecord>
        {
            Record(urlId: 1),
            Record(urlId: 1),
            Record(urlId: 2),
        };
        var processor = CreateProcessor();
        await ExecuteProcessorAsync(processor, records);

        var link1 = await _db.ShortenedUrls.FindAsync(1L);
        Assert.Equal(2, link1!.ClickCount);
        var link2 = await _db.ShortenedUrls.FindAsync(2L);
        Assert.Equal(4, link2!.ClickCount);
    }

    [Fact]
    public async Task Webhook_DispatchesClickBatch()
    {
        _db.Users.Add(new User { Id = 1, Issuer = "test", Subject = "u1", Email = "t@t.com", CreatedAtUtc = DateTime.UtcNow, LastLoginAtUtc = DateTime.UtcNow });
        _db.ShortenedUrls.Add(Link(id: 1));
        _db.Webhooks.Add(new Webhook
        {
            Id = 1,
            OwnerUserId = 1,
            Url = "https://hooks.example.com/click",
            Secret = "sec",
            EventTypes = WebhookEventTypes.LinkClicked,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var deliveryChannel = Channel.CreateUnbounded<WebhookDeliveryRecord>();
        var processor = CreateProcessor(deliveryChannel);

        await ExecuteProcessorAsync(processor, [Record(), Record()]);

        var delivery = await ReadWithTimeout(deliveryChannel.Reader, TimeSpan.FromSeconds(5));
        Assert.NotNull(delivery);
        Assert.Equal(WebhookEventTypes.LinkClicked, delivery!.EventType);

        var payload = (WebhookPayload)delivery.Payload;
        var clickData = (WebhookClickData)payload.Data;
        Assert.Equal(2, clickData.ClickDelta);
        Assert.Equal("abc1", clickData.ShortCode);
    }

    [Fact]
    public async Task Webhook_OwnerFromWorkspace_UsesWorkspaceOwner()
    {
        _db.Users.Add(new User { Id = 42, Issuer = "test", Subject = "ws-owner", Email = "ws@t.com", CreatedAtUtc = DateTime.UtcNow, LastLoginAtUtc = DateTime.UtcNow });
        _db.Workspaces.Add(new Workspace
        {
            Id = 10,
            Name = "Test Workspace",
            Slug = "test-ws",
            OwnerUserId = 42,
            CreatedAtUtc = DateTime.UtcNow
        });
        _db.ShortenedUrls.Add(new ShortenedUrl
        {
            Id = 1,
            LongUrl = "https://example.com",
            ShortCode = "ws001",
            OwnerUserId = null,
            WorkspaceId = 10,
            CreatedAtUtc = DateTime.UtcNow
        });
        _db.Webhooks.Add(new Webhook
        {
            Id = 1,
            OwnerUserId = 42,
            Url = "https://hooks.example.com/click",
            Secret = "sec",
            EventTypes = WebhookEventTypes.LinkClicked,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var deliveryChannel = Channel.CreateUnbounded<WebhookDeliveryRecord>();
        var processor = CreateProcessor(deliveryChannel);

        await ExecuteProcessorAsync(processor, [Record()]);

        var delivery = await ReadWithTimeout(deliveryChannel.Reader, TimeSpan.FromSeconds(5));
        Assert.NotNull(delivery);
        Assert.Equal(WebhookEventTypes.LinkClicked, delivery!.EventType);
    }

    [Fact]
    public async Task Sse_ChannelReceivesNotification()
    {
        _db.ShortenedUrls.Add(Link());
        await _db.SaveChangesAsync();

        var processor = CreateProcessor();
        await ExecuteProcessorAsync(processor, [Record()]);

        Assert.True(_sseChannel.Reader.TryRead(out var notification));
        Assert.NotNull(notification);
    }

    [Fact]
    public async Task Error_ExceptionInFlush_LoggedAndNextBatchContinues()
    {
        _db.ShortenedUrls.Add(Link());
        await _db.SaveChangesAsync();

        var failNext = false;
        IServiceScopeFactory controlledFactory = new ControlledScopeFactory(
            _serviceProvider, () => failNext);

        var deliveryChannel = Channel.CreateUnbounded<WebhookDeliveryRecord>();
        var processor = new ClickBatchProcessor(
            _channel,
            controlledFactory,
            NullLogger<ClickBatchProcessor>.Instance,
            new GeoIpService(Path.Combine(Path.GetTempPath(), "nonexistent.mmdb"), NullLogger<GeoIpService>.Instance),
            _sseChannel,
            new WebhookEventDispatcher(deliveryChannel, controlledFactory));

        using var cts = new CancellationTokenSource();
        var task = processor.StartAsync(cts.Token);

        failNext = true;
        await _channel.Writer.WriteAsync(Record());
        await Task.Delay(300);

        failNext = false;
        await _channel.Writer.WriteAsync(Record());
        await Task.Delay(400);

        cts.Cancel();
        await task;

        var clicks = await _db.ClickEvents.ToListAsync();
        Assert.Single(clicks);

        var link = await _db.ShortenedUrls.FindAsync(1L);
        Assert.Equal(1, link!.ClickCount);
    }

    [Fact]
    public async Task Flush_SkippedDeletedLinks_DoesNotInsertOrCount()
    {
        _db.ShortenedUrls.Add(Link(id: 1));
        await _db.SaveChangesAsync();

        var link = await _db.ShortenedUrls.FindAsync(1L)!;
        _db.ShortenedUrls.Remove(link!);
        await _db.SaveChangesAsync();

        var processor = CreateProcessor();
        await ExecuteProcessorAsync(processor, [Record()]);

        var clicks = await _db.ClickEvents.ToListAsync();
        Assert.Empty(clicks);
    }

    [Fact]
    public async Task Flush_BatchLargerThan100_FlushesAllRecords()
    {
        _db.ShortenedUrls.Add(Link());
        await _db.SaveChangesAsync();

        var records = Enumerable.Range(1, 150).Select(_ => Record()).ToList();
        var processor = CreateProcessor();

        using var cts = new CancellationTokenSource();
        var task = processor.StartAsync(cts.Token);

        foreach (var r in records)
            await _channel.Writer.WriteAsync(r);

        await Task.Delay(500);
        cts.Cancel();
        await task;

        var clicks = await _db.ClickEvents.ToListAsync();
        Assert.Equal(150, clicks.Count);
        var link = await _db.ShortenedUrls.FindAsync(1L);
        Assert.Equal(150, link!.ClickCount);
    }

    [Fact]
    public async Task ClickEvents_UserAgentParsedCorrectly()
    {
        _db.ShortenedUrls.Add(Link());
        await _db.SaveChangesAsync();

        var processor = CreateProcessor();
        var ua = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
        await ExecuteProcessorAsync(processor, [Record(ua: ua)]);

        var click = await _db.ClickEvents.SingleAsync();
        Assert.Equal(ua, click.UserAgent);
        Assert.Equal("Chrome", click.Browser);
        Assert.Equal("Windows", click.OperatingSystem);
    }

    private static async Task<T?> ReadWithTimeout<T>(ChannelReader<T> reader, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            while (await reader.WaitToReadAsync(cts.Token))
            {
                if (reader.TryRead(out var item))
                    return item;
            }
        }
        catch (OperationCanceledException) { }
        return default;
    }

    private class ControlledScopeFactory : IServiceScopeFactory
    {
        private readonly IServiceProvider _provider;
        private readonly Func<bool> _shouldFail;

        public ControlledScopeFactory(IServiceProvider provider, Func<bool> shouldFail)
        {
            _provider = provider;
            _shouldFail = shouldFail;
        }

        public IServiceScope CreateScope()
        {
            if (_shouldFail())
                throw new InvalidOperationException("Simulated scope creation failure");
            return _provider.CreateScope();
        }
    }
}
