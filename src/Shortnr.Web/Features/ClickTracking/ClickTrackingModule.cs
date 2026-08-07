namespace Shortnr.Web.Features.ClickTracking;

public static class ClickTrackingModule
{
    public static IServiceCollection AddClickTrackingFeature(this IServiceCollection services)
    {
        services.AddSingleton(Channel.CreateUnbounded<ClickRecord>());
        services.AddSingleton(Channel.CreateUnbounded<object>());
        services.AddHostedService<ClickBatchProcessor>();
        return services;
    }
}