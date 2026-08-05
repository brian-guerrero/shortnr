namespace Shortnr.Web.Models;

public class WebhookDeliveryRecord
{
    public required long WebhookId { get; init; }
    public required string EventType { get; init; }
    public required object Payload { get; init; }
}
