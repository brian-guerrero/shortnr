namespace Shortnr.Web.Features.ShortLinks;

/// <summary>
/// The preset redirect-preview themes. Shares its name vocabulary with
/// <c>BioThemes</c> so a theme means the same colors on both surfaces: "sunset",
/// "ocean", "forest" and "midnight" are Bio's themes reused here; "minimal",
/// "corporate" and "dark" originate here and are mirrored back onto Bio. The one
/// difference is the shared neutral/base palette — Bio calls it "default", the
/// preview page calls it "none" (distinct from the empty-string "skip the
/// preview page entirely" option a link/workspace can also choose).
/// </summary>
public static class PreviewThemes
{
    public const string Default = "default";

    public static readonly IReadOnlyList<string> All =
        ["default", "sunset", "ocean", "forest", "midnight", "minimal", "corporate", "dark"];

    public static bool IsValid(string? theme) =>
        theme is not null && All.Contains(theme);
}
