namespace Shortnr.Web.Features.Theming;

/// <summary>
/// Wires theming's runtime services: the community theme catalog's typed HTTP
/// client, and the <see cref="IThemeCatalog"/> registrations that let callers
/// resolve <c>IEnumerable&lt;IThemeCatalog&gt;</c> to enumerate themes from
/// every source — built-in presets via <see cref="ThemeCatalog.Instance"/> and
/// remote community themes via <see cref="CommunityThemeCatalog"/>. The static
/// <see cref="ThemeCatalog"/> vocabulary itself needs no DI wiring; only the
/// interface adapters do.
/// </summary>
public static class ThemingModule
{
    public static IServiceCollection AddThemingFeature(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<ThemeCatalogOptions>(configuration.GetSection(ThemeCatalogOptions.SectionName));
        services.AddHttpClient<ICommunityThemeCatalog, CommunityThemeCatalog>(client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("shortnr-theme-loader/1.0");
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        // IEnumerable<IThemeCatalog> resolves both sources. ThemeCatalog.Instance
        // is a stateless static-data singleton, registered directly. The
        // community catalog is exposed via a factory that resolves through the
        // ICommunityThemeCatalog registration above, so it's built by the same
        // typed-HttpClient pipeline (HttpClient, options, logger) instead of a
        // second, independently-wired construction path.
        services.AddSingleton(ThemeCatalog.Instance);
        services.AddTransient<IThemeCatalog>(sp => (IThemeCatalog)sp.GetRequiredService<ICommunityThemeCatalog>());

        return services;
    }
}
