namespace Shortnr.Web.Features.Infrastructure;

/// <summary>
/// Bound from the <c>RateLimiting</c> config section. Controls IP-keyed limits
/// on the public shorten form and redirect endpoint, plus the optional Redis
/// distributed store (see <see cref="Redis"/>).
/// </summary>
public class RateLimitingOptions
{
    public bool TrustForwardedFor { get; set; }
    public ShortenLimits Shorten { get; set; } = new();
    public RedirectLimits Redirect { get; set; } = new();
    public RedisRateLimitOptions Redis { get; set; } = new();
}

/// <summary>
/// The in-process <c>System.Threading.RateLimiting</c> store is the zero-config
/// default; <see cref="RateLimitProvider.Redis"/> opts in to the distributed
/// Redis-backed store for multi-container deployments.
/// </summary>
public enum RateLimitProvider
{
    InProcess,
    Redis
}

/// <summary>
/// Connection settings for the optional Redis-backed rate-limit store.
/// </summary>
public class RedisRateLimitOptions
{
    /// <summary>
    /// StackExchange.Redis connection string, e.g. <c>localhost:6379,abortConnect=false</c>.
    /// Empty means "not configured" (provider falls back to in-process).
    /// </summary>
    public string ConnectionString { get; set; } = "";
}

public class ShortenLimits
{
    public int PerMinute { get; set; } = 10;
    public int PerDay { get; set; } = 200;
}

public class RedirectLimits
{
    public int PerMinute { get; set; } = 300;
    public int PerDay { get; set; } = 10_000;
}
