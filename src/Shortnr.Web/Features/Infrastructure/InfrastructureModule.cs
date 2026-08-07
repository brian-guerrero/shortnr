using Microsoft.Extensions.Options;

namespace Shortnr.Web.Features.Infrastructure;

public static class InfrastructureModule
{
    public static IServiceCollection AddInfrastructureFeature(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.Configure<RateLimitingOptions>(configuration.GetSection("RateLimiting"));
        return services;
    }
}