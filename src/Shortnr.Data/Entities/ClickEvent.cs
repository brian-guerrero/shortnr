namespace Shortnr.Data.Entities;

public class ClickEvent
{
    public long Id { get; set; }
    public long ShortenedUrlId { get; set; }
    public string IpAddress { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
    public string Referer { get; set; } = string.Empty;
    public DateTime ClickedAtUtc { get; set; }

    public ShortenedUrl ShortenedUrl { get; set; } = null!;
}
