using Shortnr.Data.Entities;

namespace Shortnr.Web.Features.Social;

/// <summary>
/// Abstraction for per-platform OAuth token refresh logic. Each social-platform
/// provider (Twitter, Instagram, TikTok, YouTube) implements this to call its
/// respective token-refresh endpoint.
/// </summary>
public interface ISocialPlatformProvider
{
    /// <summary>
    /// The platform identifier (e.g. "twitter", "instagram", "tiktok", "youtube").
    /// </summary>
    string Platform { get; }

    /// <summary>
    /// Attempts to refresh the access token for the given social account using its
    /// refresh token. Returns the new tokens on success, or null on failure (e.g.
    /// revoked refresh token, invalid grant).
    /// </summary>
    Task<TokenRefreshResult?> RefreshTokenAsync(SocialAccount account, CancellationToken ct = default);
}

/// <summary>
/// Result of a successful token refresh.
/// </summary>
public sealed record TokenRefreshResult(
    string AccessToken,
    string? RefreshToken,
    DateTime? AccessTokenExpiryUtc,
    DateTime? RefreshTokenExpiryUtc);
