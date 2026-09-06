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

    /// <summary>
    /// Platform-specific redirect target (URI scheme, universal/app link) for
    /// iOS and Android user agents. When set, the redirect endpoint routes the
    /// matching platform there and falls back to <see cref="ShortenedUrl.LongUrl"/>
    /// for everyone else.
    /// </summary>
    public string? IosDeepLink { get; set; }
    public string? AndroidDeepLink { get; set; }

    /// <summary>
    /// Cached Open Graph metadata fetched from the destination URL on first
    /// share (PRD-021). Bio sub-links that are shared directly unfurl with their
    /// own title/description/image instead of a generic redirect card. The
    /// triple is refetched when <see cref="OgFetchedAtUtc"/> falls outside the
    /// configured <c>Social:UnfurlCacheHours</c> window.
    /// </summary>
    public string? OgTitle { get; set; }
    public string? OgDescription { get; set; }
    public string? OgImage { get; set; }
    public DateTime? OgFetchedAtUtc { get; set; }

    public ShortenedUrl? ShortenedUrl { get; set; }
    public PixelSnippet? PixelSnippet { get; set; }
}
