namespace Shortnr.Data.Entities;

public class WorkspaceMember
{
    public long Id { get; set; }
    public long WorkspaceId { get; set; }
    public long UserId { get; set; }
    public WorkspaceRole Role { get; set; }
    public string? InviteEmail { get; set; }
    public DateTime InvitedAtUtc { get; set; }
    public DateTime? JoinedAtUtc { get; set; }

    public Workspace? Workspace { get; set; }
    public User? User { get; set; }
}
