namespace Shortnr.Web.Features.Social;

/// <summary>
/// Aggregated data fetched from a social platform for a linked account.
/// </summary>
public sealed class SocialData
{
    /// <summary>The latest posts/clips from the platform (up to 3).</summary>
    public required IReadOnlyList<SocialPostItem> Posts { get; init; }

    /// <summary>Follower count for Twitter/Instagram/TikTok, or subscriber count for YouTube.</summary>
    public long? AudienceCount { get; init; }

    /// <summary>The display name shown on the platform.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Avatar URL from the platform.</summary>
    public string? AvatarUrl { get; init; }
}

/// <summary>
/// A single post/clip from a social platform.
/// </summary>
public sealed class SocialPostItem
{
    /// <summary>Platform-specific post ID.</summary>
    public required string ExternalPostId { get; init; }

    /// <summary>Post title (video title for TikTok/YouTube, tweet text for Twitter).</summary>
    public string? Title { get; init; }

    /// <summary>Post text content (tweet text, caption).</summary>
    public string? Text { get; init; }

    /// <summary>Thumbnail or image URL.</summary>
    public string? MediaUrl { get; init; }

    /// <summary>Direct link to the post on the platform.</summary>
    public string? Permalink { get; init; }

    /// <summary>When the post was published (UTC).</summary>
    public DateTime? PublishedAtUtc { get; init; }
}

/// <summary>
/// OAuth tokens returned after code exchange.
/// </summary>
public sealed class OAuthTokens
{
    public required string AccessToken { get; init; }
    public string? RefreshToken { get; init; }
    public DateTime? ExpiresAtUtc { get; init; }
    public string? ExternalId { get; init; }
    public string? Username { get; init; }
    public string? DisplayName { get; init; }
    public string? AvatarUrl { get; init; }
}

/// <summary>
/// Request to fetch data for a specific social account, used by the background processor.
/// </summary>
public sealed class SocialFetchRequest
{
    public required long SocialAccountId { get; init; }
}
