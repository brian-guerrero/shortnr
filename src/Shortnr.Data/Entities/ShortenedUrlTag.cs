namespace Shortnr.Data.Entities;

/// <summary>
/// A tag applied to a <see cref="ShortenedUrl"/>. Rows are written only when the
/// owner accepts a <see cref="TagSuggestion"/>, so the link's tag set is always
/// human-approved.
/// </summary>
public class ShortenedUrlTag
{
    public long Id { get; set; }
    public long ShortenedUrlId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }

    public ShortenedUrl ShortenedUrl { get; set; } = null!;
}
