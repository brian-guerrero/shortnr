namespace Shortnr.Web.Features.Theming;

/// <summary>
/// One preset theme. A theme is shared vocabulary: the same id names the same
/// palette on the bio page and on the redirect-preview page — both layouts
/// link <see cref="PreviewStylesheetPath"/> and read its <c>--pv-*</c> custom
/// properties directly, so there is exactly one CSS source per theme, not one
/// per surface. (<c>data-bio-theme</c>/<c>data-preview-theme</c> carry the id
/// for surface-specific selectors; the shared generic <c>data-theme</c>
/// attribute is what community CSS — which doesn't know which surface it's
/// on — actually targets.) Ids are persisted (<c>BioPage.Theme</c>,
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
    /// Web-root-relative path of this theme's palette stylesheet — linked
    /// unconditionally by both <c>_BioLayout.cshtml</c> and
    /// <c>_PreviewLayout.cshtml</c>, not duplicated per surface. Preset
    /// themes ship a static file under <c>wwwroot/css/themes</c>; community
    /// themes are served through the checksum-validated
    /// <c>/api/themes/community/{id}.css</c> endpoint instead, since there is
    /// no local file for them.
    /// </summary>
    public string PreviewStylesheetPath => IsCommunity
        ? $"api/themes/community/{Id}.css"
        : $"css/themes/preview-{Id}.css";
}
