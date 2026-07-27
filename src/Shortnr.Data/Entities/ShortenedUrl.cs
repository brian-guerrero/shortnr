namespace Shortnr.Data.Entities;

public class ShortenedUrl
{
    public long Id { get; set; }
    public string LongUrl { get; set; } = string.Empty;
    public string ShortCode { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public long ClickCount { get; set; }
}
