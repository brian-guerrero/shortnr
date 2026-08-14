using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Shortnr.Web.Features.Infrastructure;

public static class InfrastructureModule
{
    public static IServiceCollection AddInfrastructureFeature(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.Configure<RateLimitingOptions>(configuration.GetSection("RateLimiting"));

        // Registered unconditionally: in the InProcess (default) provider it is never used;
        // in the Redis provider it is the shared ConnectionMultiplexer the distributed
        // limiter and health check talk to.
        services.AddSingleton<RedisConnectionProvider>();
        services.AddSingleton<RateLimitLimiterFactory>();

        // The Redis health check is registered lazily (via IConfigureOptions) rather than
        // eagerly against the IConfiguration captured here: test hosts' ConfigureAppConfiguration
        // overrides only merge into builder.Configuration when builder.Build() runs, so reading
        // RateLimiting:Provider at registration time would miss a test factory's Redis override
        // and silently never register the check — leaving /health/redis mapped but empty (always
        // Healthy). This mirrors the AddDbContext lazy-resolution pattern in Program.cs.
        services.AddHealthChecks();
        services.AddSingleton<IConfigureOptions<HealthCheckServiceOptions>>(sp =>
            new ConfigureOptions<HealthCheckServiceOptions>(options =>
            {
                if (RateLimitProviderHelper.ResolveProvider(sp.GetRequiredService<IConfiguration>())
                    != RateLimitProvider.Redis)
                    return;

                options.Registrations.Add(new HealthCheckRegistration(
                    "redis",
                    _ => new RedisHealthCheck(sp.GetRequiredService<RedisConnectionProvider>()),
                    failureStatus: null,
                    tags: ["redis"]));
            }));

        return services;
    }
}
