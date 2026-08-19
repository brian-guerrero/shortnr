namespace Shortnr.Web.Features.Theming;

public interface IThemeResolver
{
    /// <summary>Every theme from every registered catalog — presets first, then community themes.</summary>
    Task<IReadOnlyList<Theme>> GetAllThemesAsync(CancellationToken ct = default);

    /// <summary>The theme with the given id from whichever catalog has it, or <see langword="null"/> if none does.</summary>
    Task<Theme?> FindAsync(string? id, CancellationToken ct = default);

    /// <summary>True if <paramref name="id"/> names a theme in any registered catalog.</summary>
    Task<bool> IsValidAsync(string? id, CancellationToken ct = default);

    /// <summary>The named theme, or <see cref="ThemeCatalog.Default"/> when the id is missing or unknown anywhere.</summary>
    Task<Theme> ResolveAsync(string? id, CancellationToken ct = default);
}

/// <summary>
/// Aggregates every registered <see cref="IThemeCatalog"/> — built-in presets
/// plus community themes — into one lookup surface, so a picker or a
/// validation check doesn't need to special-case "the static one" vs "the
/// remote one" the way the old static <c>ThemeCatalog.IsValid</c>/<c>Resolve</c>
/// callers used to (and, for the surfaces this hasn't reached yet, still do).
/// <para>
/// <see cref="FindAsync"/> deliberately has a side effect for community
/// themes: once a theme is found, it calls the owning catalog's
/// <see cref="IThemeCatalog.GetCssAsync"/>, which installs (fetches and
/// caches, in memory and to disk for <see cref="CommunityThemeCatalog"/>) the
/// theme's CSS. That means every place a theme id gets resolved — saving a
/// bio page, saving a link's preview theme, even serving a redirect — doubles
/// as an install check, on top of <see cref="CommunityThemeInstallerService"/>'s
/// startup pass. For <see cref="ThemeCatalog"/>, <see cref="IThemeCatalog.GetCssAsync"/>'s
/// default implementation is a no-op, so this costs nothing for presets.
/// </para>
/// <para>
/// <see cref="GetAllThemesAsync"/> does not do this — listing every theme for
/// a picker shouldn't download every theme's CSS, only the one actually
/// selected.
/// </para>
/// </summary>
public sealed class ThemeResolver(IEnumerable<IThemeCatalog> catalogs) : IThemeResolver
{
    public async Task<IReadOnlyList<Theme>> GetAllThemesAsync(CancellationToken ct = default)
    {
        var all = new List<Theme>();
        foreach (var catalog in catalogs)
            all.AddRange(await catalog.GetThemesAsync(ct));
        return all;
    }

    public async Task<Theme?> FindAsync(string? id, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(id)) return null;

        foreach (var catalog in catalogs)
        {
            var theme = await catalog.FindAsync(id, ct);
            if (theme is null) continue;

            if (theme.IsCommunity)
                await catalog.GetCssAsync(theme.Id, ct);
            return theme;
        }

        return null;
    }

    public async Task<bool> IsValidAsync(string? id, CancellationToken ct = default) =>
        await FindAsync(id, ct) is not null;

    public async Task<Theme> ResolveAsync(string? id, CancellationToken ct = default) =>
        await FindAsync(id, ct) ?? ThemeCatalog.Default;
}
