using uaParserLibrary;

namespace Shortnr.Web.Features.ShortLinks;

public enum MobilePlatform
{
    None,
    Ios,
    Android
}

/// <summary>
/// Minimal mobile-platform classifier built on the same ua-parser library that
/// backs the ClickEvent device columns (see <see cref="ClickBatchProcessor"/>),
/// so deep-link routing reuses existing device detection instead of duplicating
/// User-Agent parsing.
/// </summary>
public static class UserAgentDeviceDetector
{
    public static MobilePlatform GetMobilePlatform(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
            return MobilePlatform.None;

        return UAParser.GetClientInfo(userAgent)?.OS?.Name switch
        {
            "iOS" => MobilePlatform.Ios,
            "Android" => MobilePlatform.Android,
            _ => MobilePlatform.None
        };
    }

    /// <summary>
    /// Resolves the effective redirect target for a link: the platform-specific
    /// deep link when the user agent matches and one is configured, else the
    /// desktop/fallback URL.
    /// </summary>
    public static string ResolveDestination(string fallbackUrl, string? iosDeepLink, string? androidDeepLink, string? userAgent)
    {
        var platform = GetMobilePlatform(userAgent);
        if (platform == MobilePlatform.Ios && !string.IsNullOrWhiteSpace(iosDeepLink))
            return iosDeepLink;
        if (platform == MobilePlatform.Android && !string.IsNullOrWhiteSpace(androidDeepLink))
            return androidDeepLink;
        return fallbackUrl;
    }
}
