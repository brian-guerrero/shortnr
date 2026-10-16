using System.Collections.Concurrent;
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
/// <para>
/// The library exposes only a static API over shared mutable state, so the lock
/// can't be striped or moved per-instance. Because the redirect endpoint parses on
/// every hit, results are memoized by user-agent string in front of the lock: the
/// same handful of real-browser UAs absorb almost all traffic and never contend.
/// The cache is capped so high-cardinality UA strings (bots) can't grow it without
/// bound — once full, uncached UAs simply parse under the lock as before.
/// </para>
/// </summary>
public static class SafeUserAgentParser
{
    private const int CacheCapacity = 4096;

    private static readonly Lock Gate = new();
    private static readonly ConcurrentDictionary<string, UserAgentInfo?> Cache = new(StringComparer.Ordinal);
    private static int _cacheCount;

    public static UserAgentInfo? Parse(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
            return null;

        if (Cache.TryGetValue(userAgent, out var cached))
            return cached;

        UserAgentInfo? info;
        lock (Gate)
        {
            var clientInfo = UAParser.GetClientInfo(userAgent);
            info = clientInfo is null
                ? null
                : new UserAgentInfo(
                    clientInfo.Device?.Type,
                    clientInfo.Device?.Model,
                    clientInfo.OS?.Name,
                    clientInfo.OS?.Version,
                    clientInfo.Browser?.Name,
                    clientInfo.Browser?.Major,
                    clientInfo.Browser?.Version);
        }

        if (Interlocked.Increment(ref _cacheCount) <= CacheCapacity)
            Cache[userAgent] = info;
        else
            Interlocked.Decrement(ref _cacheCount);

        return info;
    }
}
