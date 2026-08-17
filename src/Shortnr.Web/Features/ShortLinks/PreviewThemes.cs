namespace Shortnr.Web.Features.ShortLinks;

/// <summary>
/// The preset redirect-preview themes. Shares its name vocabulary with
/// <c>BioThemes</c> so a theme means the same colors on both surfaces: "default",
/// "sunset", "ocean", "forest" and "midnight" are Bio's themes reused here;
/// "minimal", "corporate" and "dark" originate here and are mirrored back onto
/// Bio. "default" is the shared neutral/base palette on both surfaces — distinct
/// from the empty-string "no preview page at all" option a link/workspace can
/// also choose.
/// </summary>
public static class PreviewThemes
{
    public const string Default = "default";

    public static readonly IReadOnlyList<string> All =
        ["default", "sunset", "ocean", "forest", "midnight", "minimal", "corporate", "dark"];

    public static bool IsValid(string? theme) =>
        theme is not null && All.Contains(theme);
}
