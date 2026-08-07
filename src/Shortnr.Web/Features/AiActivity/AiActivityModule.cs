namespace Shortnr.Web.Features.AiActivity;

public static class AiActivityModule
{
    public static IServiceCollection AddAiActivityFeature(this IServiceCollection services)
    {
        services.AddSingleton(Channel.CreateUnbounded<AiActivityRecord>());
        services.AddHostedService<AiActivityProcessor>();
        return services;
    }
}