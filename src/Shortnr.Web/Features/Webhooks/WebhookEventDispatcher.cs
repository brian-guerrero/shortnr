using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;

namespace Shortnr.Web.Features.Webhooks;

public class WebhookEventDispatcher
{
    private readonly Channel<WebhookDeliveryRecord> _channel;
    private readonly IServiceScopeFactory _scopeFactory;

    public WebhookEventDispatcher(Channel<WebhookDeliveryRecord> channel, IServiceScopeFactory scopeFactory)
    {
        _channel = channel;
        _scopeFactory = scopeFactory;
    }
    public async Task DispatchLinkCreatedAsync(ShortenedUrl link, string scheme, string host)
    {
        var ownerUserId = link.OwnerUserId ?? link.Workspace?.OwnerUserId;
        if (ownerUserId is null) return;

        var domain = link.Domain?.Hostname;
        var shortUrl = domain is not null
            ? $"{scheme}://{domain}/{link.ShortCode}"
            : $"{scheme}://{host}/{link.ShortCode}";

        var payload = new WebhookPayload(
            WebhookEventTypes.LinkCreated,
            DateTime.UtcNow,
            new WebhookLinkData(
                link.ShortCode,
                shortUrl,
                link.LongUrl,
                domain,
                link.ClickCount,
                link.CreatedAtUtc));

        await EnqueueAsync(ownerUserId.Value, WebhookEventTypes.LinkCreated, payload);
    }

    public async Task DispatchLinkDeletedAsync(ShortenedUrl link, string scheme, string host)
    {
        var ownerUserId = link.OwnerUserId ?? link.Workspace?.OwnerUserId;
        if (ownerUserId is null) return;

        var domain = link.Domain?.Hostname;
        var shortUrl = domain is not null
            ? $"{scheme}://{domain}/{link.ShortCode}"
            : $"{scheme}://{host}/{link.ShortCode}";

        var payload = new WebhookPayload(
            WebhookEventTypes.LinkDeleted,
            DateTime.UtcNow,
            new WebhookDeleteData(
                link.ShortCode,
                shortUrl,
                link.LongUrl,
                domain,
                link.ClickCount,
                link.CreatedAtUtc,
                DateTime.UtcNow));

        await EnqueueAsync(ownerUserId.Value, WebhookEventTypes.LinkDeleted, payload);
    }

    public async Task DispatchLinkClickedBatchAsync(long ownerUserId, Dictionary<long, (string ShortCode, string LongUrl, string? Domain, int ClickDelta, long TotalClicks)> linkClicks, DateTime windowStart, DateTime windowEnd, string scheme, string host)
    {
        var webhooks = await LoadSubscribedWebhooksAsync(ownerUserId);
        var subscribed = webhooks
            .Where(w => WebhookEventTypes.Parse(w.EventTypes).Contains(WebhookEventTypes.LinkClicked))
            .ToList();
        if (subscribed.Count == 0) return;

        foreach (var (_, data) in linkClicks)
        {
            var shortUrl = data.Domain is not null
                ? $"{scheme}://{data.Domain}/{data.ShortCode}"
                : $"{scheme}://{host}/{data.ShortCode}";

            var payload = new WebhookPayload(
                WebhookEventTypes.LinkClicked,
                DateTime.UtcNow,
                new WebhookClickData(
                    data.ShortCode,
                    shortUrl,
                    data.LongUrl,
                    data.Domain,
                    data.ClickDelta,
                    data.TotalClicks,
                    windowStart,
                    windowEnd));

            foreach (var webhook in subscribed)
            {
                _channel.Writer.TryWrite(new WebhookDeliveryRecord
                {
                    WebhookId = webhook.Id,
                    EventType = WebhookEventTypes.LinkClicked,
                    Payload = payload
                });
            }
        }
    }

    private async Task EnqueueAsync(long ownerUserId, string eventType, object payload)
    {
        var webhooks = await LoadSubscribedWebhooksAsync(ownerUserId);

        foreach (var webhook in webhooks)
        {
            var eventTypes = WebhookEventTypes.Parse(webhook.EventTypes);
            if (eventTypes.Contains(eventType))
            {
                _channel.Writer.TryWrite(new WebhookDeliveryRecord
                {
                    WebhookId = webhook.Id,
                    EventType = eventType,
                    Payload = payload
                });
            }
        }
    }

    private async Task<List<Webhook>> LoadSubscribedWebhooksAsync(long ownerUserId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.Webhooks
            .AsNoTracking()
            .Where(w => w.OwnerUserId == ownerUserId && w.IsActive)
            .ToListAsync();
    }
}
