using Shortnr.Data.Entities;

namespace Shortnr.Web.Features.Social;

/// <summary>
/// Maps <see cref="SocialAccount"/> entities to <see cref="SocialAccountViewModel"/> view models
/// with computed token health status.
/// </summary>
public static class SocialAccountMapper
{
    /// <summary>
    /// Maps a social account to its view model, computing the token health status
    /// based on expiry times and the refresh-failed flag.
    /// </summary>
    public static SocialAccountViewModel ToViewModel(SocialAccount account, int refreshWindowHours = 24)
    {
        var health = ComputeHealth(account, refreshWindowHours);
        var description = health switch
        {
            TokenHealthStatus.Healthy => FormatHealthyDescription(account),
            TokenHealthStatus.ExpiringSoon => FormatExpiringDescription(account),
            TokenHealthStatus.RefreshFailed => "Token refresh failed — re-link required",
            _ => "Unknown status"
        };

        return new SocialAccountViewModel
        {
            Id = account.Id,
            Platform = account.Platform,
            DisplayName = account.DisplayName,
            HealthStatus = health,
            HealthDescription = description,
            AccessTokenExpiryUtc = account.AccessTokenExpiryUtc
        };
    }

    private static TokenHealthStatus ComputeHealth(SocialAccount account, int refreshWindowHours)
    {
        if (account.TokenRefreshFailed)
            return TokenHealthStatus.RefreshFailed;

        if (account.AccessTokenExpiryUtc is null)
            return TokenHealthStatus.Healthy;

        var now = DateTime.UtcNow;
        var window = DateTime.UtcNow.AddHours(refreshWindowHours);

        if (account.AccessTokenExpiryUtc <= now)
            return TokenHealthStatus.ExpiringSoon;

        if (account.AccessTokenExpiryUtc <= window)
            return TokenHealthStatus.ExpiringSoon;

        return TokenHealthStatus.Healthy;
    }

    private static string FormatHealthyDescription(SocialAccount account)
    {
        if (account.AccessTokenExpiryUtc is null)
            return "Connected";

        var remaining = account.AccessTokenExpiryUtc.Value - DateTime.UtcNow;
        if (remaining.TotalDays >= 1)
            return $"Connected, token expires in {Math.Floor(remaining.TotalDays)} days";
        if (remaining.TotalHours >= 1)
            return $"Connected, token expires in {Math.Floor(remaining.TotalHours)} hours";

        return $"Connected, token expires in {Math.Floor(remaining.TotalMinutes)} minutes";
    }

    private static string FormatExpiringDescription(SocialAccount account)
    {
        if (account.AccessTokenExpiryUtc is null)
            return "Token expiring soon, refresh scheduled";

        var remaining = account.AccessTokenExpiryUtc.Value - DateTime.UtcNow;
        if (remaining.TotalMinutes <= 0)
            return "Token expired — re-link required";

        if (remaining.TotalHours >= 1)
            return $"Token expiring soon ({Math.Floor(remaining.TotalHours)}h remaining), refresh scheduled";

        return $"Token expiring soon ({Math.Floor(remaining.TotalMinutes)}m remaining), refresh scheduled";
    }
}
