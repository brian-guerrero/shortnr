namespace Shortnr.Web.Features.Insights;

/// <summary>
/// PRD-006 AI Link Insights &amp; Auto-Tagging. Off by default: when
/// <c>AiInsights:Enabled</c> is not true, nothing is registered (no hosted
/// service, no DI services) and the /insights page returns 404.
/// </summary>
public static class AiInsightsModule
{
    public static IServiceCollection AddAiInsightsFeature(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AiInsightsOptions>(configuration.GetSection("AiInsights"));

        if (!configuration.GetValue<bool>("AiInsights:Enabled", defaultValue: false))
            return services;

        services.AddScoped<AiInsightsService>();
        services.AddHostedService<AiInsightsHostedService>();

        return services;
    }
}
