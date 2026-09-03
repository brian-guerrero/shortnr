using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;

namespace Shortnr.Web.Features.Webhooks;

public class WebhookDeliveryService : BackgroundService
{
    private const int MaxRetries = 5;
    private const int MaxResponseBytes = 1024 * 1024;

    private readonly Channel<WebhookDeliveryRecord> _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly EventBusPublisher _eventBus;
    private readonly ILogger<WebhookDeliveryService> _logger;

    public WebhookDeliveryService(
        Channel<WebhookDeliveryRecord> channel,
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory,
        EventBusPublisher eventBus,
        ILogger<WebhookDeliveryService> logger)
    {
        _channel = channel;
        _scopeFactory = scopeFactory;
        _httpClientFactory = httpClientFactory;
        _eventBus = eventBus;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("WebhookDeliveryService starting");

        try
        {
            await foreach (var record in _channel.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await DeliverAsync(record, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error delivering webhook {WebhookId} for event {EventType}",
                        record.WebhookId, record.EventType);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }

        _logger.LogInformation("WebhookDeliveryService stopping");
    }

    private async Task DeliverAsync(WebhookDeliveryRecord record, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var webhook = await db.Webhooks.FirstOrDefaultAsync(w => w.Id == record.WebhookId, ct);
        if (webhook is null || !webhook.IsActive)
        {
            _logger.LogDebug("Webhook {WebhookId} not found or inactive, skipping delivery", record.WebhookId);
            return;
        }

        var eventTypes = WebhookEventTypes.Parse(webhook.EventTypes);
        if (!eventTypes.Contains(record.EventType))
        {
            _logger.LogDebug("Webhook {WebhookId} not subscribed to {EventType}, skipping",
                record.WebhookId, record.EventType);
            return;
        }

        var payloadJson = JsonSerializer.Serialize(record.Payload);
        var signature = WebhookSigningService.Sign(payloadJson, webhook.Secret);

        var client = _httpClientFactory.CreateClient("WebhookDelivery");
        var request = new HttpRequestMessage(HttpMethod.Post, webhook.Url)
        {
            Content = new StringContent(payloadJson, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Shortnr-Signature", signature);
        request.Headers.Add("X-Shortnr-Event", record.EventType);
        request.Headers.UserAgent.ParseAdd("Shortnr-Webhook/1.0");

        var maxDelay = TimeSpan.FromSeconds(30);
        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(10));

                var response = await client.SendAsync(request, cts.Token);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Webhook {WebhookId} delivered successfully to {Url}",
                        webhook.Id, webhook.Url);

                    if (webhook.FailureCount > 0)
                    {
                        webhook.FailureCount = 0;
                        webhook.LastFailureAtUtc = null;
                        await db.SaveChangesAsync(ct);
                    }

                    // Fan the successful delivery out to the distributed event bus (PRD-018)
                    // as a webhook.fired event. No-op when EventBus:Provider=InProcess; swallowed
                    // on broker failure so delivery success is not affected.
                    await _eventBus.PublishAsync(EventBusRoutingKeys.WebhookFired,
                        new WebhookFiredData(webhook.Id, record.EventType, webhook.Url, DateTime.UtcNow));
                    return;
                }

                _logger.LogWarning("Webhook {WebhookId} delivery failed with status {StatusCode}",
                    webhook.Id, response.StatusCode);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Webhook {WebhookId} delivery attempt {Attempt} failed",
                    webhook.Id, attempt + 1);
            }

            if (attempt < MaxRetries)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                if (delay > maxDelay) delay = maxDelay;
                await Task.Delay(delay, ct);
            }
        }

        webhook.FailureCount++;
        webhook.LastFailureAtUtc = DateTime.UtcNow;
        if (webhook.FailureCount >= MaxRetries)
        {
            webhook.IsActive = false;
            _logger.LogWarning("Webhook {WebhookId} disabled after {FailureCount} consecutive failures",
                webhook.Id, webhook.FailureCount);
        }
        await db.SaveChangesAsync(ct);
    }
}
