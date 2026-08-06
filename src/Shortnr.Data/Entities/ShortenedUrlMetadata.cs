namespace Shortnr.Data.Entities;

/// <summary>
/// Optional 1:1 side-table holding Smart Link (PRD-005) metadata that would
/// otherwise widen the core <see cref="ShortenedUrl"/> row: UTM campaign
/// components, the retargeting pixel reference and platform deep-link targets.
/// Rows are created only when at least one piece of Smart Link metadata is set.
/// </summary>
public class ShortenedUrlMetadata
{
    public long Id { get; set; }
    public long ShortenedUrlId { get; set; }

    public string? UtmSource { get; set; }
    public string? UtmMedium { get; set; }
    public string? UtmCampaign { get; set; }
    public string? UtmTerm { get; set; }
    public string? UtmContent { get; set; }

    public long? PixelSnippetId { get; set; }
    /// <summary>
    /// For template-based snippets this is the marketer's pixel ID substituted
    /// into the template's <c>{{PIXEL_ID}}</c> placeholder; for a custom snippet
    /// it holds the full pasted snippet HTML, emitted verbatim.
    /// </summary>
    public string? PixelId { get; set; }

    public ShortenedUrl? ShortenedUrl { get; set; }
    public PixelSnippet? PixelSnippet { get; set; }
}
