namespace Shortnr.Web.Features.Authentication;

public static class AuthenticationModule
{
    public static IServiceCollection AddAuthenticationFeature(
        this IServiceCollection services, IConfiguration config, IWebHostEnvironment env)
    {
        services.AddSingleton(Channel.CreateUnbounded<PendingUserLogin>());
        services.AddHostedService<UserProvisioningProcessor>();
        services.AddScoped<UserIdentityService>();
        services.AddScoped<IUserIdentityService>(sp => sp.GetRequiredService<UserIdentityService>());
        services.AddOidcAuthentication(config, env);
        return services;
    }
}