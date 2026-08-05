namespace Shortnr.Data.Entities;

public class Webhook
{
    public long Id { get; set; }
    public long OwnerUserId { get; set; }
    public string Url { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;
    public string EventTypes { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LastFailureAtUtc { get; set; }
    public int FailureCount { get; set; }

    public User? Owner { get; set; }
}
