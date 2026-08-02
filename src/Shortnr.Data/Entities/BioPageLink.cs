namespace Shortnr.Data.Entities;

public class BioPageLink
{
    public long Id { get; set; }
    public long BioPageId { get; set; }
    public long ShortenedUrlId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? IconUrl { get; set; }
    public int SortOrder { get; set; }
    public bool IsVisible { get; set; } = true;

    public BioPage? BioPage { get; set; }
    public ShortenedUrl? ShortenedUrl { get; set; }
}
