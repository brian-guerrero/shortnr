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
/// <param name="Author">Optional author, populated for community themes.</param>
/// <param name="Description">Optional one-line description, populated for community themes.</param>
/// <param name="IsCommunity">
/// True when this theme came from <see cref="CommunityThemeCatalog"/> rather
/// than the built-in <see cref="ThemeCatalog"/>. Changes how
/// <see cref="PreviewStylesheetPath"/> resolves, since community themes have
/// no file under <c>wwwroot/css/themes</c>.
/// </param>
public sealed record Theme(
    string Id,
    string Label,
    bool IsDark,
    string? Author = null,
    string? Description = null,
    bool IsCommunity = false)
{
    /// <summary>
    /// Web-root-relative path of this theme's redirect-preview palette
    /// stylesheet. Preset themes ship a static file under
    /// <c>wwwroot/css/themes</c>; community themes are served through the
    /// checksum-validated <c>/api/themes/community/{id}.css</c> endpoint
    /// instead, since there is no local file for them.
    /// </summary>
    public string PreviewStylesheetPath => IsCommunity
        ? $"api/themes/community/{Id}.css"
        : $"css/themes/preview-{Id}.css";
}
