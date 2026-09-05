namespace Shortnr.Web.Features.Social;

/// <summary>
/// Health status of a social account's OAuth tokens, surfaced in the UI.
/// </summary>
public enum TokenHealthStatus
{
    /// <summary>Token is valid and not expiring soon.</summary>
    Healthy,
    /// <summary>Token expires within the refresh window; background refresh is in progress.</summary>
    ExpiringSoon,
    /// <summary>Token refresh failed (revoked, invalid grant, etc.); re-link required.</summary>
    RefreshFailed
}

/// <summary>
/// View model for the /bio/social page, representing a single linked social account.
/// </summary>
public sealed class SocialAccountViewModel
{
    public long Id { get; init; }
    public string Platform { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public TokenHealthStatus HealthStatus { get; init; }
    /// <summary>
    /// Human-readable description of the token status (e.g. "Connected, token expires in 2 hours").
    /// </summary>
    public string HealthDescription { get; init; } = string.Empty;
    public DateTime? AccessTokenExpiryUtc { get; init; }
}
