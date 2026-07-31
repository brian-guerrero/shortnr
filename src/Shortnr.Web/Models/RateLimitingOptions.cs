namespace Shortnr.Web.Models;

/// <summary>
/// Bound from the <c>RateLimiting</c> config section. Controls IP-keyed limits
/// on the public shorten form and redirect endpoint.
/// </summary>
public class RateLimitingOptions
{
    public bool TrustForwardedFor { get; set; }
    public ShortenLimits Shorten { get; set; } = new();
    public RedirectLimits Redirect { get; set; } = new();
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
