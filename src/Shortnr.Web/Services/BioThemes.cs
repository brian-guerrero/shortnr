namespace Shortnr.Web.Services;

/// <summary>
/// The preset bio-page themes. The public bio page maps a theme to a
/// <c>data-theme</c> attribute plus Pico CSS variable overrides; the editor
/// just needs the same canonical list so the dropdown never diverges.
/// </summary>
public static class BioThemes
{
    public static readonly IReadOnlyList<string> All =
        ["default", "sunset", "ocean", "forest", "midnight", "brutal"];

    public static bool IsValid(string? theme) =>
        theme is not null && All.Contains(theme);
}
