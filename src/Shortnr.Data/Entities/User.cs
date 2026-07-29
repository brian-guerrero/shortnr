namespace Shortnr.Data.Entities;

public class User
{
    public long Id { get; set; }
    public string Issuer { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Name { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime LastLoginAtUtc { get; set; }

    public ICollection<ShortenedUrl> ShortenedUrls { get; set; } = [];
}
