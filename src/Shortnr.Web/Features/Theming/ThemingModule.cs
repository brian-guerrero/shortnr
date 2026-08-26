namespace Shortnr.Web.Features.Theming;

/// <summary>
/// Wires theming's runtime services: the community theme catalog (a singleton
/// so its manifest/CSS caches are actually shared across the app, not
/// re-fetched on every DI resolution), the <see cref="IThemeCatalog"/>
/// registrations that let callers resolve <c>IEnumerable&lt;IThemeCatalog&gt;</c>
/// to enumerate themes from every source, the <see cref="IThemeResolver"/>
/// aggregate built on top of that, and <see cref="CommunityThemeInstallerService"/>,
/// which ensures in-use community themes are installed at startup. The static
/// <see cref="ThemeCatalog"/> vocabulary itself needs no DI wiring; only the
/// interface adapters and the community-theme machinery do.
/// </summary>
public static class ThemingModule
{
    public static IServiceCollection AddThemingFeature(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<ThemeCatalogOptions>(configuration.GetSection(ThemeCatalogOptions.SectionName));

        services.AddHttpClient("community-themes", client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("shortnr-theme-loader/1.0");
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        // Singleton (rather than a typed HttpClient's default Transient
        // lifetime) so the manifest fetch and the CSS memory/disk caches are
        // genuinely shared across the app's lifetime instead of being rebuilt
        // — and re-fetching the manifest over the network — on every DI
        // resolution. IHttpClientFactory.CreateClient(...) is still called per
        // outgoing request internally, so this doesn't reintroduce the
        // "singleton HttpClient never rotates its handler" DNS-staleness trap.
        services.AddSingleton<CommunityThemeCatalog>();
        services.AddSingleton<ICommunityThemeCatalog>(sp => sp.GetRequiredService<CommunityThemeCatalog>());

        // IEnumerable<IThemeCatalog> resolves both sources: built-in presets
        // via the stateless ThemeCatalog.Instance singleton, and community
        // themes via the same CommunityThemeCatalog singleton registered
        // above (not a second instance — that would double the manifest
        // fetch and split the CSS cache in two).
        services.AddSingleton(ThemeCatalog.Instance);
        services.AddSingleton<IThemeCatalog>(sp => sp.GetRequiredService<CommunityThemeCatalog>());

        services.AddSingleton<IThemeResolver, ThemeResolver>();
        services.AddHostedService<CommunityThemeInstallerService>();

        return services;
    }
}
