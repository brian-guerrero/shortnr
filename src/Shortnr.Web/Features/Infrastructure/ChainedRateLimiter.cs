using System.Threading.RateLimiting;

namespace Shortnr.Web.Features.Infrastructure;

/// <summary>
/// Combines multiple rate limiters so every inner limiter must permit a request.
/// Used to stack a per-minute burst window and a per-day cap on the same key.
/// Fixed-window limiters auto-replenish, so disposing an unaccepted lease is a
/// no-op and there is no permit leak on the chain.
/// </summary>
public sealed class ChainedRateLimiter(RateLimiter[] limiters) : RateLimiter
{
    public override TimeSpan? IdleDuration => null;

    protected override RateLimitLease AttemptAcquireCore(int permitCount)
    {
        var acquired = new List<RateLimitLease>(limiters.Length);
        try
        {
            foreach (var limiter in limiters)
            {
                var lease = limiter.AttemptAcquire(permitCount);
                if (!lease.IsAcquired)
                {
                    foreach (var previous in acquired)
                        previous.Dispose();
                    return lease;
                }

                acquired.Add(lease);
            }

            return new CompositeLease(acquired);
        }
        catch
        {
            foreach (var previous in acquired)
                previous.Dispose();
            throw;
        }
    }

    protected override ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken) =>
        AcquireCoreAsync(permitCount, cancellationToken);

    public override RateLimiterStatistics? GetStatistics() => null;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var limiter in limiters)
                limiter.Dispose();
        }
        base.Dispose(disposing);
    }

    private async ValueTask<RateLimitLease> AcquireCoreAsync(int permitCount, CancellationToken ct = default)
    {
        var acquired = new List<RateLimitLease>(limiters.Length);
        try
        {
            foreach (var limiter in limiters)
            {
                var lease = await limiter.AcquireAsync(permitCount, ct);

                if (!lease.IsAcquired)
                {
                    foreach (var previous in acquired)
                        previous.Dispose();
                    return lease;
                }

                acquired.Add(lease);
            }

            return new CompositeLease(acquired);
        }
        catch
        {
            foreach (var previous in acquired)
                previous.Dispose();
            throw;
        }
    }

    private sealed class CompositeLease(List<RateLimitLease> leases) : RateLimitLease
    {
        public override bool IsAcquired => true;

        public override IEnumerable<string> MetadataNames => leases.SelectMany(l => l.MetadataNames);

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            foreach (var lease in leases)
            {
                if (lease.TryGetMetadata(metadataName, out metadata))
                    return true;
            }
            metadata = null;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var lease in leases)
                    lease.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
