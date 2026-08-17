namespace Shortnr.Web.Features.Theming;

/// <summary>
/// The single source of truth for shortnr's preset themes. Bio pages and the
/// redirect-preview page both read this catalog; each surface used to keep its
/// own hand-mirrored copy of the name list, so every new theme had to be added
/// twice and the two could silently drift.
/// <para>
/// Adding, renaming or removing a theme is a one-place edit here plus its two
/// palettes: <c>wwwroot/css/themes/preview-&lt;id&gt;.css</c> and the
/// <c>[data-bio-theme="&lt;id&gt;"]</c> block in <c>Pages/Bio/_BioLayout.cshtml</c>.
/// <c>ThemeCatalogTests</c> fails the build's test run if either is missing.
/// </para>
/// All themes share the brutalist structural treatment (DSG-002 §5) — sharp
/// corners, hard offset shadows, bold borders, Archivo Black headings — and
/// differ only in palette.
/// </summary>
public static class ThemeCatalog
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
}
