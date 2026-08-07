using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace Shortnr.Web.Features.ShortLinks;

/// <summary>
/// Enforces the per-IP shorten-form limit (<c>Shorten:PerMinute</c> / <c>Shorten:PerDay</c>)
/// directly inside <c>IndexModel.OnPost</c>. Razor Pages do not surface
/// <c>[EnableRateLimiting]</c> from handler methods as endpoint metadata, and applying the
/// attribute to the page class would also throttle the landing page GET, so the shorten limit
/// is acquired manually rather than through the rate-limit middleware. The redirect endpoint
/// uses the middleware policy instead (<c>RequireRateLimiting("redirect-ip")</c> on a minimal
/// API endpoint, where the metadata mechanism does work).
/// </summary>
public sealed class ShortenRateLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<HttpContext> _limiter;

    public ShortenRateLimiter(IOptions<RateLimitingOptions> options)
    {
        var limits = options.Value;
        _limiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            RateLimitPartition.Get(
                IpRateLimitPolicies.ResolveKey(context, limits.TrustForwardedFor),
                _ => IpRateLimitPolicies.Build(limits.Shorten.PerMinute, limits.Shorten.PerDay)));
    }

    public async ValueTask<bool> TryAcquireAsync(HttpContext context, CancellationToken ct = default)
    {
        using var lease = await _limiter.AcquireAsync(context, 1, ct);
        return lease.IsAcquired;
    }

    public void Dispose() => _limiter.Dispose();
}
