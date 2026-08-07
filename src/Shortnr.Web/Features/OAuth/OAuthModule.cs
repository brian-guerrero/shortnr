namespace Shortnr.Web.Features.OAuth;

public static class OAuthModule
{
    public static IServiceCollection AddOAuthFeature(
        this IServiceCollection services, IConfiguration config, IWebHostEnvironment env)
    {
        services.AddOAuthServer(config, env);
        return services;
    }
}