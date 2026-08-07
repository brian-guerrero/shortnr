using System.Threading.RateLimiting;

namespace Shortnr.Web.Features.Domains;

/// <summary>
/// Shared construction for the per-IP chained rate limiter (per-minute burst window
/// stacked with a per-day cap) used by both the redirect middleware policy and the
/// manual shorten-form limiter, so the two enforcement points stay consistent.
/// </summary>
public static class IpRateLimitPolicies
{
    public static ChainedRateLimiter Build(int perMinute, int perDay) => new(
    [
        new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = perMinute,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }),
        new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = perDay,
            Window = TimeSpan.FromDays(1),
            QueueLimit = 0,
            AutoReplenishment = true
        })
    ]);

    public static string ResolveKey(HttpContext context, bool trustForwardedFor) =>
        ClientIpResolver.Resolve(context, trustForwardedFor);
}
