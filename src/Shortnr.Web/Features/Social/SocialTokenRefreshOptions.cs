namespace Shortnr.Web.Features.Social;

/// <summary>
/// Configuration for the social token background refresh scheduler.
/// Reads from the <c>Social:TokenRefresh</c> config section.
/// </summary>
public class SocialTokenRefreshOptions
{
    /// <summary>
    /// Whether the background token refresh scheduler is enabled. Default false.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// How often (in hours) the refresh scheduler runs. Default 6.
    /// </summary>
    public int IntervalHours { get; set; } = 6;

    /// <summary>
    /// Refresh accounts whose access token expires within this many hours. Default 24.
    /// </summary>
    public int RefreshWindowHours { get; set; } = 24;
}
