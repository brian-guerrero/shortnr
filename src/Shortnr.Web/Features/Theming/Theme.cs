namespace Shortnr.Web.Features.Theming;

/// <summary>
/// One preset theme. A theme is shared vocabulary: the same id names the same
/// palette on the bio page (<c>data-bio-theme</c>) and on the redirect-preview
/// page (<c>data-preview-theme</c>). Ids are persisted (<c>BioPage.Theme</c>,
/// <c>ShortenedUrl.PreviewTheme</c>, <c>Workspace.DefaultPreviewTheme</c>), so
/// renaming one is a data migration, not just a catalog edit.
/// </summary>
/// <param name="Id">Persisted, attribute- and URL-safe identifier.</param>
/// <param name="Label">Human-readable name for the theme pickers.</param>
/// <param name="IsDark">
/// True when the palette is dark, so the preview layout can pin
/// <c>data-color-scheme</c> without re-listing the dark themes itself.
/// </param>
public sealed record Theme(string Id, string Label, bool IsDark)
{
    /// <summary>Web-root-relative path of this theme's redirect-preview palette stylesheet.</summary>
    public string PreviewStylesheetPath => $"css/themes/preview-{Id}.css";
}
