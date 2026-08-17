namespace Shortnr.Data.Entities;

public class ShortenedUrl
{
    public long Id { get; set; }
    public string LongUrl { get; set; } = string.Empty;
    public string ShortCode { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public long ClickCount { get; set; }
    public long? OwnerUserId { get; set; }
    public long? DomainId { get; set; }
    public long? WorkspaceId { get; set; }

    /// <summary>User-facing title (nullable; set via edit form / PATCH).</summary>
    public string? Title { get; set; }

    /// <summary>User-facing description (nullable; set via edit form / PATCH).</summary>
    public string? Description { get; set; }

    /// <summary>Set when the link is archived. Archived links do not redirect.</summary>
    public DateTime? ArchivedAtUtc { get; set; }

    /// <summary>Last edit timestamp; null until the first edit (PRD-024).</summary>
    public DateTime? UpdatedAtUtc { get; set; }

    /// <summary>Preview theme for the redirect interstitial (PRD-022). Null means
    /// "fall back to workspace default, then 'minimal'".</summary>
    public string? PreviewTheme { get; set; }

    public ICollection<ClickEvent> ClickEvents { get; set; } = [];
    public ICollection<ShortenedUrlTag> Tags { get; set; } = [];
    public ICollection<TagSuggestion> TagSuggestions { get; set; } = [];
    public User? Owner { get; set; }
    public Domain? Domain { get; set; }
    public Workspace? Workspace { get; set; }
    public ShortenedUrlMetadata? Metadata { get; set; }

    public string DisplayUrl() => Domain?.Hostname is { Length: > 0 } host
        ? $"//{host}/{ShortCode}"
        : $"/{ShortCode}";

    public string DisplayText() => Domain?.Hostname is { Length: > 0 } host
        ? $"{host}/{ShortCode}"
        : $"/{ShortCode}";

    public bool IsArchived => ArchivedAtUtc is not null;
}
