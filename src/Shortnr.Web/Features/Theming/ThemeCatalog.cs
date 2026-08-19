namespace Shortnr.Web.Features.Theming;

/// <summary>
/// The single source of truth for shortnr's preset themes. Bio pages and the
/// redirect-preview page both read this catalog; each surface used to keep its
/// own hand-mirrored copy of the name list (and, until the palettes were
/// unified onto shared <c>--pv-*</c> custom properties, its own hand-mirrored
/// copy of every color too), so every new theme had to be added twice and the
/// two could silently drift.
/// <para>
/// Adding, renaming or removing a theme is a one-place edit here plus its
/// palette: <c>wwwroot/css/themes/preview-&lt;id&gt;.css</c> (see
/// <see cref="Theme.PreviewStylesheetPath"/>), linked unconditionally by both
/// <c>_BioLayout.cshtml</c> and <c>_PreviewLayout.cshtml</c> — one file, not
/// one per surface. <c>ThemeCatalogTests</c> fails the build's test run if
/// it's missing.
/// </para>
/// All themes share the brutalist structural treatment (DSG-002 §5) — sharp
/// corners, hard offset shadows, bold borders, Archivo Black headings — and
/// differ only in palette.
/// </summary>
public sealed class ThemeCatalog : IThemeCatalog
{
    /// <summary>
    /// Id of the shared neutral/base palette. Distinct from the empty-string
    /// preview option a link or workspace can also choose, which means "no
    /// preview page at all" rather than a themed one.
    /// </summary>
    public const string DefaultId = "default";

    public static readonly IReadOnlyList<Theme> All =
    [
        new(DefaultId, "Default", IsDark: false),
        new("sunset", "Sunset", IsDark: false),
        new("ocean", "Ocean", IsDark: false),
        new("forest", "Forest", IsDark: false),
        new("midnight", "Midnight", IsDark: true),
        new("minimal", "Minimal", IsDark: false),
        new("corporate", "Corporate", IsDark: false),
        new("dark", "Dark", IsDark: true),
    ];

    /// <summary>Theme ids in catalog order — the vocabulary both surfaces validate against.</summary>
    public static readonly IReadOnlyList<string> Ids = [.. All.Select(t => t.Id)];

    public static Theme Default { get; } = All[0];

    public static Theme? Find(string? id) =>
        id is null ? null : All.FirstOrDefault(t => t.Id == id);

    public static bool IsValid(string? id) => Find(id) is not null;

    /// <summary>The named theme, or <see cref="Default"/> when the id is missing or unknown.</summary>
    public static Theme Resolve(string? id) => Find(id) ?? Default;

    /// <summary>
    /// Adapts this static catalog to <see cref="IThemeCatalog"/> so DI
    /// consumers can resolve <c>IEnumerable&lt;IThemeCatalog&gt;</c> alongside
    /// <see cref="CommunityThemeCatalog"/> without special-casing "the static
    /// one". Purely additive — every member above keeps working exactly as
    /// today for all existing static call sites.
    /// </summary>
    public static readonly IThemeCatalog Instance = new ThemeCatalog();

    private ThemeCatalog() { }

    Task<IReadOnlyList<Theme>> IThemeCatalog.GetThemesAsync(CancellationToken ct) => Task.FromResult(All);

    Task<Theme?> IThemeCatalog.FindAsync(string? id, CancellationToken ct) => Task.FromResult(Find(id));

    Task<bool> IThemeCatalog.IsValidAsync(string? id, CancellationToken ct) => Task.FromResult(IsValid(id));

    // IThemeCatalog.GetCssAsync is intentionally not overridden — the
    // interface's default implementation (returns null) is correct here:
    // preset CSS is served as static files under wwwroot/css/themes, not
    // fetched through the catalog.
}
