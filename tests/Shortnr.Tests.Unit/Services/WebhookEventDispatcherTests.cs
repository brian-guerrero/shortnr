using System.Threading.Channels;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Web.Features.Webhooks;

namespace Shortnr.Tests.Unit.Services;

public class WebhookEventDispatcherTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly IServiceScopeFactory _scopeFactory;

    public WebhookEventDispatcherTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
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
    // LinkCreated dispatch
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DispatchLinkCreatedAsync_SubscribedWebhook_EnqueuesCorrectPayload()
    {
        var user = SeedUser();
        var webhook = SeedWebhook(user.Id, "link.created");
        var link = new ShortenedUrl
        {
            LongUrl = "https://example.com/page",
            ShortCode = "abc123",
            OwnerUserId = user.Id,
            ClickCount = 7,
            CreatedAtUtc = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc)
        };

        var channel = Channel.CreateUnbounded<WebhookDeliveryRecord>();
        var dispatcher = new WebhookEventDispatcher(channel, _scopeFactory);

        await dispatcher.DispatchLinkCreatedAsync(link, "https", "short.test");

        var record = await channel.Reader.ReadAsync();
        Assert.Equal(webhook.Id, record.WebhookId);
        Assert.Equal(WebhookEventTypes.LinkCreated, record.EventType);

        var payload = (WebhookPayload)record.Payload;
        Assert.Equal(WebhookEventTypes.LinkCreated, payload.Event);
        var data = (WebhookLinkData)payload.Data;
        Assert.Equal("abc123", data.ShortCode);
        Assert.Equal("https://short.test/abc123", data.ShortUrl);
        Assert.Equal("https://example.com/page", data.LongUrl);
        Assert.Null(data.Domain);
        Assert.Equal(7, data.ClickCount);
        Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc), data.CreatedAtUtc);
    }

    [Fact]
    public async Task DispatchLinkCreatedAsync_BrandedDomain_UsesDomainInShortUrl()
    {
        var user = SeedUser();
        var domain = new Domain { Hostname = "go.example.com", OwnerUserId = user.Id, IsVerified = true, CreatedAtUtc = DateTime.UtcNow };
        _db.Domains.Add(domain);
        _db.SaveChanges();
        var webhook = SeedWebhook(user.Id, "link.created");
        var link = new ShortenedUrl
        {
            LongUrl = "https://example.com/page",
            ShortCode = "abc123",
            OwnerUserId = user.Id,
            ClickCount = 0,
            CreatedAtUtc = DateTime.UtcNow,
            DomainId = domain.Id,
            Domain = domain
        };

        var channel = Channel.CreateUnbounded<WebhookDeliveryRecord>();
        var dispatcher = new WebhookEventDispatcher(channel, _scopeFactory);

        await dispatcher.DispatchLinkCreatedAsync(link, "https", "short.test");

        var record = await channel.Reader.ReadAsync();
        var data = (WebhookLinkData)((WebhookPayload)record.Payload).Data;
        Assert.Equal("go.example.com", data.Domain);
        Assert.Equal("https://go.example.com/abc123", data.ShortUrl);
    }

    // -------------------------------------------------------------------------
    // LinkDeleted dispatch
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DispatchLinkDeletedAsync_SubscribedWebhook_EnqueuesDeletePayload()
    {
        var user = SeedUser();
        var webhook = SeedWebhook(user.Id, "link.deleted");
        var link = new ShortenedUrl
        {
            LongUrl = "https://example.com/page",
            ShortCode = "abc123",
            OwnerUserId = user.Id,
            ClickCount = 3,
            CreatedAtUtc = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc)
        };

        var channel = Channel.CreateUnbounded<WebhookDeliveryRecord>();
        var dispatcher = new WebhookEventDispatcher(channel, _scopeFactory);

        await dispatcher.DispatchLinkDeletedAsync(link, "https", "short.test");

        var record = await channel.Reader.ReadAsync();
        Assert.Equal(webhook.Id, record.WebhookId);
        Assert.Equal(WebhookEventTypes.LinkDeleted, record.EventType);

        var payload = (WebhookPayload)record.Payload;
        Assert.Equal(WebhookEventTypes.LinkDeleted, payload.Event);
        var data = (WebhookDeleteData)payload.Data;
        Assert.Equal("abc123", data.ShortCode);
        Assert.Equal("https://short.test/abc123", data.ShortUrl);
        Assert.Equal(3, data.ClickCount);
        Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc), data.CreatedAtUtc);
        Assert.NotEqual(default, data.DeletedAtUtc);
    }

    // -------------------------------------------------------------------------
    // LinkClickedBatch dispatch (multiple links / fan-out)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DispatchLinkClickedBatchAsync_MultipleLinks_EnqueuesOneRecordPerLink()
    {
        var user = SeedUser();
        var webhook = SeedWebhook(user.Id, "link.clicked");
        var windowStart = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var windowEnd = new DateTime(2026, 1, 1, 0, 5, 0, DateTimeKind.Utc);
        var clicks = new Dictionary<long, (string ShortCode, string LongUrl, string? Domain, int ClickDelta, long TotalClicks)>
        {
            [1] = ("code1", "https://example.com/1", "go.example.com", 3, 10),
            [2] = ("code2", "https://example.com/2", null, 1, 5)
        };

        var channel = Channel.CreateUnbounded<WebhookDeliveryRecord>();
        var dispatcher = new WebhookEventDispatcher(channel, _scopeFactory);

        await dispatcher.DispatchLinkClickedBatchAsync(user.Id, clicks, windowStart, windowEnd, "https", "short.test");

        var records = new List<WebhookDeliveryRecord>();
        for (int i = 0; i < 2; i++)
            records.Add(await channel.Reader.ReadAsync());

        Assert.All(records, r =>
        {
            Assert.Equal(webhook.Id, r.WebhookId);
            Assert.Equal(WebhookEventTypes.LinkClicked, r.EventType);
            var data = (WebhookClickData)((WebhookPayload)r.Payload).Data;
            Assert.Equal(windowStart, data.WindowStart);
            Assert.Equal(windowEnd, data.WindowEnd);
        });

        var byCode = records.ToDictionary(
            r => ((WebhookClickData)((WebhookPayload)r.Payload).Data).ShortCode,
            r => (WebhookClickData)((WebhookPayload)r.Payload).Data);

        Assert.Equal("https://go.example.com/code1", byCode["code1"].ShortUrl);
        Assert.Equal("go.example.com", byCode["code1"].Domain);
        Assert.Equal(3, byCode["code1"].ClickDelta);
        Assert.Equal(10, byCode["code1"].TotalClicks);

        Assert.Equal("https://short.test/code2", byCode["code2"].ShortUrl);
        Assert.Null(byCode["code2"].Domain);
        Assert.Equal(1, byCode["code2"].ClickDelta);
        Assert.Equal(5, byCode["code2"].TotalClicks);
    }

    // -------------------------------------------------------------------------
    // Owner resolution
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DispatchLinkCreatedAsync_LinkOwnedByUser_DispatchesToUserWebhook()
    {
        var user = SeedUser();
        var webhook = SeedWebhook(user.Id, "link.created");
        var otherUser = SeedUser();
        var otherWebhook = SeedWebhook(otherUser.Id, "link.created");
        var link = new ShortenedUrl
        {
            LongUrl = "https://example.com/page",
            ShortCode = "abc123",
            OwnerUserId = user.Id,
            ClickCount = 0,
            CreatedAtUtc = DateTime.UtcNow
        };

        var channel = Channel.CreateUnbounded<WebhookDeliveryRecord>();
        var dispatcher = new WebhookEventDispatcher(channel, _scopeFactory);

        await dispatcher.DispatchLinkCreatedAsync(link, "https", "short.test");

        var record = await channel.Reader.ReadAsync();
        Assert.Equal(webhook.Id, record.WebhookId);
        Assert.NotEqual(otherWebhook.Id, record.WebhookId);
    }

    [Fact]
    public async Task DispatchLinkCreatedAsync_LinkOwnedByWorkspace_DispatchesToWorkspaceOwnerWebhook()
    {
        var owner = SeedUser();
        var workspace = new Workspace
        {
            Name = "Acme",
            Slug = "acme",
            OwnerUserId = owner.Id,
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.Workspaces.Add(workspace);
        _db.SaveChanges();

        var webhook = SeedWebhook(owner.Id, "link.created");
        var link = new ShortenedUrl
        {
            LongUrl = "https://example.com/page",
            ShortCode = "abc123",
            OwnerUserId = null,
            WorkspaceId = workspace.Id,
            Workspace = workspace,
            ClickCount = 0,
            CreatedAtUtc = DateTime.UtcNow
        };

        var channel = Channel.CreateUnbounded<WebhookDeliveryRecord>();
        var dispatcher = new WebhookEventDispatcher(channel, _scopeFactory);

        await dispatcher.DispatchLinkCreatedAsync(link, "https", "short.test");

        var record = await channel.Reader.ReadAsync();
        Assert.Equal(webhook.Id, record.WebhookId);
    }

    [Fact]
    public async Task DispatchLinkCreatedAsync_NoResolvableOwner_DoesNotDispatch()
    {
        var link = new ShortenedUrl
        {
            LongUrl = "https://example.com/page",
            ShortCode = "abc123",
            OwnerUserId = null,
            ClickCount = 0,
            CreatedAtUtc = DateTime.UtcNow
        };

        var channel = Channel.CreateUnbounded<WebhookDeliveryRecord>();
        var dispatcher = new WebhookEventDispatcher(channel, _scopeFactory);

        await dispatcher.DispatchLinkCreatedAsync(link, "https", "short.test");

        Assert.False(channel.Reader.TryRead(out _));
    }

    // -------------------------------------------------------------------------
    // No dispatch when no active webhooks
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DispatchLinkCreatedAsync_NoActiveWebhooksForOwner_DoesNotDispatch()
    {
        var user = SeedUser();
        var link = new ShortenedUrl
        {
            LongUrl = "https://example.com/page",
            ShortCode = "abc123",
            OwnerUserId = user.Id,
            ClickCount = 0,
            CreatedAtUtc = DateTime.UtcNow
        };

        var channel = Channel.CreateUnbounded<WebhookDeliveryRecord>();
        var dispatcher = new WebhookEventDispatcher(channel, _scopeFactory);

        await dispatcher.DispatchLinkCreatedAsync(link, "https", "short.test");

        Assert.False(channel.Reader.TryRead(out _));
    }

    [Fact]
    public async Task DispatchLinkCreatedAsync_OnlyInactiveWebhookExists_DoesNotDispatch()
    {
        var user = SeedUser();
        SeedWebhook(user.Id, "link.created", isActive: false);
        var link = new ShortenedUrl
        {
            LongUrl = "https://example.com/page",
            ShortCode = "abc123",
            OwnerUserId = user.Id,
            ClickCount = 0,
            CreatedAtUtc = DateTime.UtcNow
        };

        var channel = Channel.CreateUnbounded<WebhookDeliveryRecord>();
        var dispatcher = new WebhookEventDispatcher(channel, _scopeFactory);

        await dispatcher.DispatchLinkCreatedAsync(link, "https", "short.test");

        Assert.False(channel.Reader.TryRead(out _));
    }

    [Fact]
    public async Task DispatchLinkClickedBatchAsync_NoSubscribedWebhooks_DoesNotDispatch()
    {
        var user = SeedUser();
        var windowStart = DateTime.UtcNow.AddMinutes(-5);
        var windowEnd = DateTime.UtcNow;
        var clicks = new Dictionary<long, (string ShortCode, string LongUrl, string? Domain, int ClickDelta, long TotalClicks)>
        {
            [1] = ("code1", "https://example.com/1", null, 3, 10)
        };

        var channel = Channel.CreateUnbounded<WebhookDeliveryRecord>();
        var dispatcher = new WebhookEventDispatcher(channel, _scopeFactory);

        await dispatcher.DispatchLinkClickedBatchAsync(user.Id, clicks, windowStart, windowEnd, "https", "short.test");

        Assert.False(channel.Reader.TryRead(out _));
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private User SeedUser()
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
        return user;
    }

    private Webhook SeedWebhook(long ownerUserId, string eventTypes, bool isActive = true)
    {
        var webhook = new Webhook
        {
            OwnerUserId = ownerUserId,
            Url = "https://example.com/hook",
            Secret = WebhookSigningService.GenerateSecret(),
            EventTypes = eventTypes,
            IsActive = isActive,
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.Webhooks.Add(webhook);
        _db.SaveChanges();
        return webhook;
    }
}
