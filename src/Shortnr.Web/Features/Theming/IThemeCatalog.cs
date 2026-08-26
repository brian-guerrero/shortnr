namespace Shortnr.Web.Features.Theming;

/// <summary>
/// A source of themes. Both the built-in preset catalog
/// (<see cref="ThemeCatalog.Instance"/>) and the remote
/// <see cref="CommunityThemeCatalog"/> implement this so callers can resolve
/// <c>IEnumerable&lt;IThemeCatalog&gt;</c> from DI and enumerate every theme
/// source without special-casing "the static one" vs "the remote one".
/// <para>
/// This is a read-only projection: it exposes exactly what a theme picker
/// needs (id, label, palette metadata) as the shared <see cref="Theme"/>
/// record, plus an optional CSS fetch for sources whose palettes aren't
/// static <c>wwwroot</c> files. It intentionally does not carry the extra
/// manifest fields (version, checksum, download URL) that
/// <see cref="ICommunityThemeCatalog"/> exposes for the community-theme
/// endpoints — those are an implementation detail of how community themes are
/// fetched and verified, not part of the shared theme vocabulary.
/// </para>
/// <para>
/// Every member is async even though <see cref="ThemeCatalog"/>'s data is
/// entirely in-memory, because <see cref="CommunityThemeCatalog"/>'s is not:
/// it fetches and caches a remote manifest. A synchronous interface would
/// force one of the two implementations into a shape that doesn't fit it.
/// </para>
/// </summary>
public interface IThemeCatalog
{
    /// <summary>All themes this catalog currently offers.</summary>
    Task<IReadOnlyList<Theme>> GetThemesAsync(CancellationToken ct = default);

    /// <summary>The theme with the given id, or <see langword="null"/> if this catalog doesn't have one.</summary>
    Task<Theme?> FindAsync(string? id, CancellationToken ct = default);

    /// <summary>True if <paramref name="id"/> names a theme in this catalog.</summary>
    Task<bool> IsValidAsync(string? id, CancellationToken ct = default);

    /// <summary>
    /// The theme's redirect-preview CSS, when this catalog serves it directly
    /// rather than through a static <c>wwwroot</c> file (as
    /// <see cref="ThemeCatalog"/>'s presets do). The default implementation
    /// returns <see langword="null"/>; <see cref="CommunityThemeCatalog"/>
    /// overrides it with its checksum-validated fetch.
    /// </summary>
    Task<string?> GetCssAsync(string id, CancellationToken ct = default) => Task.FromResult<string?>(null);
}
