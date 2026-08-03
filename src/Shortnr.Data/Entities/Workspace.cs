namespace Shortnr.Data.Entities;

public class Workspace
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public long OwnerUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public User? Owner { get; set; }
    public ICollection<WorkspaceMember> Members { get; set; } = [];
    public ICollection<ShortenedUrl> ShortenedUrls { get; set; } = [];
}
