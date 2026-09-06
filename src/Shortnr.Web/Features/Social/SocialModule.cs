using System.Threading.Channels;
using Shortnr.Data.Entities;

namespace Shortnr.Web.Features.Social;

/// <summary>
/// Feature module for social platform integrations (PRD-021).
/// Registers the provider implementations, cache, background processor,
/// and HTTP client for social platform API calls.
/// </summary>
public static class SocialModule
{
    public static IServiceCollection AddSocialFeature(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<SocialCacheOptions>(config.GetSection("Social:Cache"));
        services.AddSingleton<ISocialCache, SocialCache>();
        services.AddSingleton(Channel.CreateUnbounded<SocialFetchRequest>());
        services.AddHostedService<SocialFetchProcessor>();
        services.AddScoped<SocialAccountService>();
        services.AddScoped<OgFetcherService>();

        // Register platform providers
        services.AddSingleton<ISocialPlatformProvider, TwitterSocialProvider>();
        services.AddSingleton<ISocialPlatformProvider, InstagramSocialProvider>();
        services.AddSingleton<ISocialPlatformProvider, YouTubeSocialProvider>();
        services.AddSingleton<ISocialPlatformProvider, TikTokSocialProvider>();

        services.AddHttpClient("SocialPlatforms", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.MaxResponseContentBufferSize = 1024 * 1024;
        });

        return services;
    }
}
