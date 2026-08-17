namespace Shortnr.Web.Features.BioPages;

/// <summary>
/// The preset bio-page themes. All themes use brutalist styling (DSG-002 §5):
/// sharp corners, hard offset shadows, bold borders, and Archivo Black headings.
/// Each theme has its own color palette but shares the same structural treatment.
/// The public bio page maps a theme to a <c>data-bio-theme</c> attribute plus
/// a set of <c>--bio-*</c> custom properties; the editor just needs the same
/// canonical list so the dropdown never diverges.
/// "default", "sunset", "ocean", "forest" and "midnight" originate here and are
/// also offered as redirect-preview themes (see <c>PreviewThemes</c>, where the
/// "default" palette is exposed as "none"); "minimal", "corporate" and "dark"
/// originate on the preview side and are mirrored here so both surfaces share
/// one theme-name vocabulary.
/// </summary>
public static class BioThemes
{
    public static readonly IReadOnlyList<string> All =
        ["default", "sunset", "ocean", "forest", "midnight", "minimal", "corporate", "dark"];

    public static bool IsValid(string? theme) =>
        theme is not null && All.Contains(theme);
}
