using System.Threading.RateLimiting;
using StackExchange.Redis;

namespace Shortnr.Web.Features.Infrastructure;

/// <summary>
/// A fixed-window <see cref="RateLimiter"/> backed by a Redis counter, used by
/// <see cref="RateLimitLimiterFactory"/> when <c>RateLimiting:Provider=Redis</c>.
/// <para>
/// Each acquisition runs an atomic <c>INCR</c> + <c>EXPIRE</c> Lua script against a key
/// scoped to the policy and identifier (<c>shortnr:ratelimit:{policy}:{identifier}:{window}</c>),
/// with the key TTL matching the policy window (PRD-017 Requirement 3). Counters therefore
/// live in Redis and are shared across every container pointing at the same instance — the
/// actual distributed behaviour PRD-017 exists for.
/// </para>
/// <para>
/// Graceful degradation (PRD-017 Requirement 4): if Redis is unreachable, the request is
/// served by an in-process <see cref="FixedWindowRateLimiter"/> fallback with a throttled
/// warning log — never a 500.
/// </para>
/// </summary>
public sealed class RedisRateLimiter : RateLimiter
{
    // INCR then set the TTL only when the key is created, so the window starts at the first
    // request; on later requests PTTL gives the remaining window for Retry-After metadata.
    private const string IncrementScript = """
        local current = redis.call('INCR', KEYS[1])
        local ttl = -1
        if current == 1 then
            ttl = tonumber(ARGV[1])
            redis.call('EXPIRE', KEYS[1], ttl)
        else
            ttl = redis.call('PTTL', KEYS[1])
        end
        return { current, ttl }
        """;

    private static readonly TimeSpan WarningThrottleWindow = TimeSpan.FromSeconds(30);

    private readonly RedisConnectionProvider _redis;
    private readonly string _fullKey;
    private readonly int _permitLimit;
    private readonly TimeSpan _window;
    private readonly ILogger _logger;
    private readonly FixedWindowRateLimiter _fallback;
    private long _lastWarningLoggedAtTicks;

    public RedisRateLimiter(
        RedisConnectionProvider redis,
        string policy,
        string identifier,
        int permitLimit,
        TimeSpan window,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(redis);
        ArgumentException.ThrowIfNullOrWhiteSpace(policy);
        ArgumentNullException.ThrowIfNull(identifier);

        _redis = redis;
        _fullKey = $"{RedisConnectionProvider.KeyPrefix}:{policy}:{identifier}:{(long)window.TotalSeconds}";
        _permitLimit = permitLimit;
        _window = window;
        _logger = logger;
        _fallback = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = window,
            QueueLimit = 0,
            AutoReplenishment = true
        });
    }

    /// <summary>The Redis key this limiter counts against, for diagnostics and tests.</summary>
    public string FullKey => _fullKey;

    public override TimeSpan? IdleDuration => null;

    protected override RateLimitLease AttemptAcquireCore(int permitCount) =>
        TryAcquireCore(permitCount, () => _fallback.AttemptAcquire(permitCount));

    protected override async ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken)
    {
        try
        {
            if (!_redis.TryGetDatabase(out var database))
            {
                LogFallbackWarning(null);
                return await _fallback.AcquireAsync(permitCount, cancellationToken);
            }

            var result = await database!.ScriptEvaluateAsync(
                IncrementScript,
                new RedisKey[] { _fullKey },
                new RedisValue[] { (long)_window.TotalSeconds });

            return ToLease(result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogFallbackWarning(ex);
            return await _fallback.AcquireAsync(permitCount, cancellationToken);
        }
    }

    public override RateLimiterStatistics? GetStatistics() => null;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _fallback.Dispose();
        base.Dispose(disposing);
    }

    private RateLimitLease TryAcquireCore(int permitCount, Func<RateLimitLease> fallback)
    {
        try
        {
            if (!_redis.TryGetDatabase(out var database))
            {
                LogFallbackWarning(null);
                return fallback();
            }

            var result = database!.ScriptEvaluate(
                IncrementScript,
                new RedisKey[] { _fullKey },
                new RedisValue[] { (long)_window.TotalSeconds });

            return ToLease(result);
        }
        catch (Exception ex)
        {
            LogFallbackWarning(ex);
            return fallback();
        }
    }

    private RateLimitLease ToLease(RedisResult result)
    {
        var values = (RedisResult[]?)result ?? throw new InvalidOperationException(
            "Redis rate-limit script returned a non-array result.");
        var current = (long)values[0];
        var remainingTtlMs = (long)values[1];

        if (current <= 0)
            throw new InvalidOperationException($"Redis rate-limit script returned a non-positive count '{current}'.");

        var remaining = Math.Max(0, _permitLimit - (int)current);
        return current <= _permitLimit
            ? RedisRateLimitLease.Acquired()
            : RedisRateLimitLease.Rejected(TimeSpan.FromMilliseconds(Math.Max(1, remainingTtlMs)));
    }

    private void LogFallbackWarning(Exception? exception)
    {
        var now = DateTime.UtcNow.Ticks;
        var last = Interlocked.Read(ref _lastWarningLoggedAtTicks);
        if (now - last < WarningThrottleWindow.Ticks)
            return;
        if (Interlocked.CompareExchange(ref _lastWarningLoggedAtTicks, now, last) != last)
            return;

        if (exception is not null)
            _logger.LogWarning(exception,
                "Redis rate-limit store unavailable for key {Key}; falling back to in-process limiting.",
                _fullKey);
        else
            _logger.LogWarning(
                "Redis rate-limit store is not connected for key {Key}; falling back to in-process limiting.",
                _fullKey);
    }

    private sealed class RedisRateLimitLease : RateLimitLease
    {
        // Matches System.Threading.RateLimiting.MetadataName.RetryAfter.Name so any consumer
        // reading RETRY_AFTER (e.g. the middleware or a future Retry-After writer) sees the
        // same key the in-process FixedWindowRateLimiter exposes.
        private const string RetryAfterMetadataName = "RETRY_AFTER";
        private const string ReasonPhraseMetadataName = "REASON_PHRASE";

        private readonly bool _acquired;
        private readonly TimeSpan? _retryAfter;

        private RedisRateLimitLease(bool acquired, TimeSpan? retryAfter)
        {
            _acquired = acquired;
            _retryAfter = retryAfter;
        }

        public static RedisRateLimitLease Acquired() => new(true, null);

        public static RedisRateLimitLease Rejected(TimeSpan retryAfter) =>
            new(false, retryAfter);

        public override bool IsAcquired => _acquired;

        public override IEnumerable<string> MetadataNames =>
            _retryAfter is null ? [ReasonPhraseMetadataName] : [RetryAfterMetadataName, ReasonPhraseMetadataName];

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            switch (metadataName)
            {
                case RetryAfterMetadataName when _retryAfter is { } retryAfter:
                    metadata = retryAfter;
                    return true;
                case ReasonPhraseMetadataName:
                    if (_acquired)
                    {
                        metadata = null;
                        return false;
                    }
                    metadata = "Fixed window reached. Permit limit exceeded.";
                    return true;
                default:
                    metadata = null;
                    return false;
            }
        }
    }
}
