namespace Shortnr.Web.Features.Social;

/// <summary>
/// PRD-027 Social Account Token Encryption &amp; Refresh Scheduling.
/// Registers the token encryption service, social account CRUD service,
/// per-platform providers, and (when enabled) the background refresh scheduler.
/// </summary>
public static class SocialModule
{
    public static IServiceCollection AddSocialFeature(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SocialTokenRefreshOptions>(configuration.GetSection("Social:TokenRefresh"));

        services.AddScoped<SocialTokenEncryptionService>();
        services.AddScoped<SocialAccountService>();

        // Per-platform providers (always registered; the scheduler only calls them when enabled)
        services.AddSingleton<ISocialPlatformProvider, TwitterSocialPlatformProvider>();
        services.AddSingleton<ISocialPlatformProvider, InstagramSocialPlatformProvider>();
        services.AddSingleton<ISocialPlatformProvider, TiktokSocialPlatformProvider>();
        services.AddSingleton<ISocialPlatformProvider, YoutubeSocialPlatformProvider>();

        // HTTP clients for each platform's token-refresh endpoints
        services.AddHttpClient("social-twitter");
        services.AddHttpClient("social-instagram");
        services.AddHttpClient("social-tiktok");
        services.AddHttpClient("social-youtube");

        if (!configuration.GetValue<bool>("Social:TokenRefresh:Enabled", defaultValue: false))
            return services;

        services.AddHostedService<SocialTokenRefreshHostedService>();

        return services;
    }
}
