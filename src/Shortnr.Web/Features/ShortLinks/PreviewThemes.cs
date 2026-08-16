namespace Shortnr.Web.Features.ShortLinks;

public static class PreviewThemes
{
    public const string Default = "minimal";

    public static readonly IReadOnlyList<string> All =
        ["minimal", "brutal", "corporate", "dark"];

    public static bool IsValid(string? theme) =>
        theme is not null && All.Contains(theme);
}
