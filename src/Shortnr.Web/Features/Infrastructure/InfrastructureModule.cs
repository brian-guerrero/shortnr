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

        if (RateLimitProviderHelper.ResolveProvider(configuration) == RateLimitProvider.Redis)
        {
            services.AddHealthChecks()
                .AddCheck<RedisHealthCheck>("redis", tags: ["redis"]);
        }

        return services;
    }
}
