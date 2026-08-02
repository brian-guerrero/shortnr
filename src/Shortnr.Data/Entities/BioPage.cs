namespace Shortnr.Data.Entities;

public class BioPage
{
    public long Id { get; set; }
    public long OwnerUserId { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string? BioText { get; set; }
    public string Theme { get; set; } = "default";
    public DateTime CreatedAtUtc { get; set; }

    public User? Owner { get; set; }
    public ICollection<BioPageLink> Links { get; set; } = [];
}
