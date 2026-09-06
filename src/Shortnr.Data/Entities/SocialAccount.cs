namespace Shortnr.Data.Entities;

/// <summary>
/// A creator's linked social profile (Twitter/X, Instagram, TikTok or YouTube)
/// used to power the dynamic bio sections (PRD-021). Exactly one of
/// <see cref="OwnerUserId"/> or <see cref="WorkspaceId"/> is set, mirroring the
/// ownership split used by <see cref="ShortenedUrl"/> and <see cref="Domain"/>.
/// Tokens are never stored in plaintext — they are protected via ASP.NET Data
/// Protection before being persisted in the <c>*Encrypted</c> columns.
/// </summary>
public class SocialAccount
{
    public long Id { get; set; }
    public SocialProvider Provider { get; set; }
    public long? OwnerUserId { get; set; }
    public long? WorkspaceId { get; set; }

    public string ExternalId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? AvatarUrl { get; set; }

    /// <summary>Follower count for Twitter/Instagram/TikTok.</summary>
    public long? FollowerCount { get; set; }

    /// <summary>Subscriber count for YouTube.</summary>
    public long? SubscriberCount { get; set; }

    /// <summary>Data-Protection-protected OAuth access token (base64 string).</summary>
    public string? AccessTokenEncrypted { get; set; }

    /// <summary>Data-Protection-protected OAuth refresh token (base64 string).</summary>
    public string? RefreshTokenEncrypted { get; set; }

    public DateTime? TokenExpiresUtc { get; set; }

    public bool IsLinked { get; set; }

    /// <summary>Human-readable message from the most recent failed fetch, if any.</summary>
    public string? LastError { get; set; }

    public DateTime? LastSuccessUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public User? Owner { get; set; }
    public Workspace? Workspace { get; set; }
    public ICollection<SocialPost> Posts { get; set; } = [];
}