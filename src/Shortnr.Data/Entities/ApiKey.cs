namespace Shortnr.Data.Entities;

public class ApiKey
{
    public long Id { get; set; }
    public long OwnerUserId { get; set; }
    public string KeyHash { get; set; } = string.Empty;
    public string KeyPrefix { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    public User? Owner { get; set; }
}
