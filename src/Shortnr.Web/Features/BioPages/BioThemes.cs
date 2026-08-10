namespace Shortnr.Web.Features.BioPages;

/// <summary>
/// The preset bio-page themes. The public bio page maps a theme to a
/// <c>data-bio-theme</c> attribute plus a set of <c>--bio-*</c> custom
/// properties; the editor just needs the same canonical list so the dropdown
/// never diverges. <c>brutal</c> is the neo-brutalist theme (DSG-002 §5) and
/// sits alongside the five soft themes rather than replacing them.
/// </summary>
public static class BioThemes
{
    public static readonly IReadOnlyList<string> All =
        ["default", "sunset", "ocean", "forest", "midnight", "brutal"];

    public static bool IsValid(string? theme) =>
        theme is not null && All.Contains(theme);
}
