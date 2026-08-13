using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Shortnr.Web.Features.Infrastructure;

/// <summary>
/// Owns the <see cref="ConnectionMultiplexer"/> for the optional Redis-backed rate-limit
/// store. Registered unconditionally (even when <c>RateLimiting:Provider=InProcess</c>,
/// where it is never touched), so <see cref="RateLimitLimiterFactory"/> can hold it without
/// provider-conditional DI. Connecting is lazy and never blocks startup: with
/// <c>abortConnect=false</c> the multiplexer returns immediately and reconnects in the
/// background, so a downed Redis degrades to in-process limiting instead of 500s
/// (PRD-017 Requirement 4). It is <b>not</b> a general-purpose cache — it only ever
/// serves rate-limit counters (PRD-017 Non-goal).
/// </summary>
public sealed class RedisConnectionProvider : IDisposable
{
    /// <summary>Namespace prefix for every rate-limit key, per PRD-017 Requirement 3.</summary>
    public const string KeyPrefix = "shortnr:ratelimit";

    private readonly string _connectionString;
    private readonly ILogger<RedisConnectionProvider> _logger;
    private readonly Lazy<ConnectionMultiplexer> _multiplexer;
    private int _startupWarningLogged;

    public RedisConnectionProvider(IOptions<RateLimitingOptions> options, ILogger<RedisConnectionProvider> logger)
    {
        _connectionString = options.Value.Redis?.ConnectionString ?? string.Empty;
        _logger = logger;
        // PublicationOnly: if Connect() throws (bad endpoint/credentials) the exception is
        // not cached, so a later request retries rather than being permanently wedged.
        _multiplexer = new Lazy<ConnectionMultiplexer>(Connect, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>True when a connection string was supplied (provider opted in).</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_connectionString);

    /// <summary>True when the multiplexer has an established, healthy connection.</summary>
    public bool IsConnected => IsConfigured && _multiplexer.IsValueCreated && _multiplexer.Value.IsConnected;

    /// <summary>Throws when no connection string is configured.</summary>
    public IDatabase GetDatabase() => Multiplexer.GetDatabase();

    /// <summary>
    /// Best-effort database access used by the rate limiter's fast-degradation path: returns
    /// <c>false</c> (with a throttled warning) instead of throwing when Redis is down or was
    /// never reachable, so the caller can fall back to in-process limiting immediately.
    /// </summary>
    public bool TryGetDatabase(out IDatabase? database)
    {
        database = null;
        try
        {
            var mux = Multiplexer;
            if (!mux.IsConnected)
                return false;
            database = mux.GetDatabase();
            return true;
        }
        catch (Exception ex)
        {
            LogStartupWarning(ex);
            return false;
        }
    }

    /// <summary>
    /// Connectivity probe for the Redis health check. Returns <c>true</c> when a round-trip
    /// ping succeeds, otherwise <c>false</c> with the error message.
    /// </summary>
    public bool TryPing(out string? error)
    {
        error = null;
        try
        {
            GetDatabase().Ping();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private ConnectionMultiplexer Multiplexer
    {
        get
        {
            if (!IsConfigured)
                throw new InvalidOperationException(
                    "No Redis connection string configured. Set 'RateLimiting:Redis:ConnectionString' or leave " +
                    "'RateLimiting:Provider' at 'InProcess'.");
            return _multiplexer.Value;
        }
    }

    private ConnectionMultiplexer Connect()
    {
        var configuration = ConfigurationOptions.Parse(_connectionString);
        // Graceful degradation (PRD-017 Requirement 4): never fail app startup or a request
        // because Redis is unreachable — the multiplexer reconnects in the background and
        // commands fail over to the in-process limiter until it does.
        configuration.AbortOnConnectFail = false;

        var mux = ConnectionMultiplexer.Connect(configuration);
        mux.ConnectionFailed += (_, e) =>
        {
            if (e.Exception is not null)
                _logger.LogWarning(e.Exception,
                    "Redis connection failed ({FailureType}). Rate limiting falls back to in-process until the connection is restored.",
                    e.FailureType);
            else
                _logger.LogWarning(
                    "Redis connection failed ({FailureType}). Rate limiting falls back to in-process until the connection is restored.",
                    e.FailureType);
        };
        mux.ConnectionRestored += (_, _) =>
            _logger.LogWarning("Redis connection restored. Distributed rate limiting is active again.");
        return mux;
    }

    private void LogStartupWarning(Exception ex)
    {
        if (Interlocked.Exchange(ref _startupWarningLogged, 1) == 1)
            return;
        _logger.LogWarning(ex,
            "Redis rate-limit store is unavailable (connection string '{ConnectionString}'). " +
            "Rate limiting falls back to in-process until it can connect.",
            string.IsNullOrEmpty(_connectionString) ? "<empty>" : MaskConnectionString(_connectionString));
    }

    /// <summary>Redacts credentials in the connection string before logging it.</summary>
    private static string MaskConnectionString(string connectionString)
    {
        var parts = connectionString.Split(',');
        return string.Join(',', parts.Select(part =>
            part.StartsWith("password=", StringComparison.OrdinalIgnoreCase) ? "password=***" : part));
    }

    public void Dispose()
    {
        // Only touch the multiplexer if it was actually created — forcing Value on an
        // InProcess setup would attempt a (pointless) connection to an empty string.
        if (_multiplexer.IsValueCreated)
            _multiplexer.Value.Dispose();
    }
}
