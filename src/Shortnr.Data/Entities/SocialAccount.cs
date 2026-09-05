namespace Shortnr.Data.Entities;

public class SocialAccount
{
    public long Id { get; set; }
    public long OwnerUserId { get; set; }
    /// <summary>
    /// Platform identifier: "twitter", "instagram", "tiktok", "youtube".
    /// </summary>
    public string Platform { get; set; } = string.Empty;
    /// <summary>
    /// Platform-specific account identifier (e.g. Twitter user ID).
    /// </summary>
    public string PlatformAccountId { get; set; } = string.Empty;
    /// <summary>
    /// Display name shown in the UI (e.g. "@username" or display name).
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;
    /// <summary>
    /// Encrypted access token (ciphertext via ASP.NET Data Protection).
    /// </summary>
    public string AccessTokenEncrypted { get; set; } = string.Empty;
    /// <summary>
    /// Encrypted refresh token (ciphertext via ASP.NET Data Protection). Null for platforms that don't use refresh tokens.
    /// </summary>
    public string? RefreshTokenEncrypted { get; set; }
    /// <summary>
    /// UTC timestamp when the access token expires. Null if unknown.
    /// </summary>
    public DateTime? AccessTokenExpiryUtc { get; set; }
    /// <summary>
    /// UTC timestamp when the refresh token expires. Null if unknown or no refresh token.
    /// </summary>
    public DateTime? RefreshTokenExpiryUtc { get; set; }
    /// <summary>
    /// Set to true when the last token refresh attempt failed (e.g. revoked token, invalid grant).
    /// The UI surfaces "re-link required" when this is true.
    /// </summary>
    public bool TokenRefreshFailed { get; set; }
    /// <summary>
    /// UTC timestamp of the last successful token refresh. Null if never refreshed.
    /// </summary>
    public DateTime? LastRefreshedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public User? Owner { get; set; }
}
