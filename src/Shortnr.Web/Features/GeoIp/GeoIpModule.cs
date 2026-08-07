namespace Shortnr.Web.Features.GeoIp;

public static class GeoIpModule
{
    public static IServiceCollection AddGeoIpFeature(this IServiceCollection services)
    {
        services.AddHttpClient("GeoIpUpdate", (sp, client) =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            client.Timeout = TimeSpan.FromMinutes(5);
            var accountId = config["GeoIp:MaxMindAccountId"];
            if (!string.IsNullOrWhiteSpace(accountId))
                client.DefaultRequestHeaders.UserAgent.ParseAdd($"shortnr/1.0 (account_id: {accountId})");
        });

        services.AddSingleton(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var logger = sp.GetRequiredService<ILogger<GeoIpService>>();
            var configuredPath = config["GeoIp:DatabasePath"];
            var path = !string.IsNullOrWhiteSpace(configuredPath)
                ? configuredPath
                : Path.Combine(sp.GetRequiredService<IWebHostEnvironment>().WebRootPath, "data", "GeoLite2-City.mmdb");
            return new GeoIpService(path, logger);
        });

        services.AddHostedService<GeoIpUpdateService>();
        return services;
    }
}