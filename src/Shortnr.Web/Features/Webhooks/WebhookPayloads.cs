namespace Shortnr.Web.Features.Webhooks;

public record WebhookPayload(
    string Event,
    DateTime Timestamp,
    object Data);

public record WebhookLinkData(
    string ShortCode,
    string ShortUrl,
    string LongUrl,
    string? Domain,
    long ClickCount,
    DateTime CreatedAtUtc);

public record WebhookClickData(
    string ShortCode,
    string ShortUrl,
    string LongUrl,
    string? Domain,
    int ClickDelta,
    long TotalClicks,
    DateTime WindowStart,
    DateTime WindowEnd);

public record WebhookDeleteData(
    string ShortCode,
    string ShortUrl,
    string LongUrl,
    string? Domain,
    long ClickCount,
    DateTime CreatedAtUtc,
    DateTime DeletedAtUtc);
