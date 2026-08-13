using System.Threading.RateLimiting;

namespace Shortnr.Web.Features.Domains;

/// <summary>
/// Shared construction for the per-IP chained rate limiter (per-minute burst window
/// stacked with a per-day cap) used by both the redirect middleware policy and the
/// manual shorten-form limiter, so the two enforcement points stay consistent. The
/// limiter itself is built by the infrastructure module's <see cref="RateLimitLimiterFactory"/>,
/// which honours <c>RateLimiting:Provider</c> (in-process default vs. distributed Redis).
/// </summary>
public static class IpRateLimitPolicies
{
    public static RateLimiter Build(
        Infrastructure.RateLimitLimiterFactory factory,
        string policy,
        string identifier,
        int perMinute,
        int perDay) =>
        factory.BuildChain(
            policy, identifier,
            perMinute, TimeSpan.FromMinutes(1),
            perDay, TimeSpan.FromDays(1));

    public static string ResolveKey(HttpContext context, bool trustForwardedFor) =>
        ClientIpResolver.Resolve(context, trustForwardedFor);
}
