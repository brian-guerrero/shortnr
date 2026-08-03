namespace Shortnr.Data.Entities;

public class Domain
{
    public long Id { get; set; }
    public string Hostname { get; set; } = string.Empty;
    public long? OwnerUserId { get; set; }
    public long? WorkspaceId { get; set; }
    public bool IsVerified { get; set; }
    public bool IsDefault { get; set; }
    public string VerificationToken { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }

    public User? Owner { get; set; }
    public Workspace? Workspace { get; set; }
}
