namespace Shortnr.Web.Features.ShortLinks;

public static class ShortLinksModule
{
    public static IServiceCollection AddShortLinksFeature(this IServiceCollection services)
    {
        services.AddSingleton<QrService>();
        services.AddSingleton<ShortenRateLimiter>();
        services.AddSingleton<BulkLinkUndoService>();
        services.AddScoped<ViewRenderService>();
        return services;
    }
}