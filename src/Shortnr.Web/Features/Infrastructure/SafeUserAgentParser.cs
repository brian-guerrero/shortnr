using uaParserLibrary;

namespace Shortnr.Web.Features.Infrastructure;

/// <summary>
/// Immutable snapshot of the User-Agent fields we persist or route on.
/// </summary>
public sealed record UserAgentInfo(
    string? DeviceType,
    string? DeviceModel,
    string? OsName,
    string? OsVersion,
    string? BrowserName,
    string? BrowserMajor,
    string? BrowserVersion);

/// <summary>
/// Serializes access to <see cref="UAParser"/>, which is not thread-safe: it keeps
/// parse state in shared statics and hands back a reused result instance, so
/// concurrent callers observe each other's user agents (a redirect gets classified
/// from another request's UA). Both the lock and the copy-out below are required —
/// reading the returned instance outside the lock still races. Every call into the
/// library must go through here.
/// </summary>
public static class SafeUserAgentParser
{
    private static readonly Lock Gate = new();

    public static UserAgentInfo? Parse(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
            return null;

        lock (Gate)
        {
            var info = UAParser.GetClientInfo(userAgent);
            if (info is null)
                return null;

            return new UserAgentInfo(
                info.Device?.Type,
                info.Device?.Model,
                info.OS?.Name,
                info.OS?.Version,
                info.Browser?.Name,
                info.Browser?.Major,
                info.Browser?.Version);
        }
    }
}
