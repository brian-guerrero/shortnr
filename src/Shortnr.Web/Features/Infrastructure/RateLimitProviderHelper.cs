using Microsoft.Extensions.Configuration;

namespace Shortnr.Web.Features.Infrastructure;

/// <summary>
/// Resolves <c>RateLimiting:Provider</c> (<c>InProcess</c> | <c>Redis</c>). When the
/// value is omitted, defaults to <see cref="RateLimitProvider.InProcess"/> so the
/// zero-config in-process limiter from PRD-010 stays the baseline. Unsupported values
/// fail fast at startup — mirroring <c>DatabaseProviderHelper.ResolveProvider</c> so a
/// typo surfaces immediately instead of silently degrading the intended configuration.
/// </summary>
public static class RateLimitProviderHelper
{
    public const string ConfigSection = "RateLimiting";
    public const string ProviderKey = "Provider";

    public static RateLimitProvider ResolveProvider(IConfiguration configuration)
    {
        var value = configuration[$"{ConfigSection}:{ProviderKey}"];

        if (string.IsNullOrWhiteSpace(value))
            return RateLimitProvider.InProcess;

        return value.Trim().ToLowerInvariant() switch
        {
            "inprocess" or "in-process" => RateLimitProvider.InProcess,
            "redis" => RateLimitProvider.Redis,
            _ => throw new InvalidOperationException(
                $"Unsupported '{ConfigSection}:{ProviderKey}' value '{value}'. " +
                "Supported values: InProcess, Redis.")
        };
    }
}