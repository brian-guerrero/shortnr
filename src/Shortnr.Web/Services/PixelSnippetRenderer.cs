using Shortnr.Data;
using Shortnr.Data.Entities;

namespace Shortnr.Web.Services;

/// <summary>
/// Builds the client-side HTML/JS a retargeting interstitial must fire. Template
/// snippets substitute the link's pixel ID into the <c>{{PIXEL_ID}}</c>
/// placeholder; custom snippets are emitted verbatim.
/// </summary>
public static class PixelSnippetRenderer
{
    /// <summary>Renders the snippet for a link, or an empty string when there is
    /// nothing concrete to fire (custom snippet with no pasted HTML, or a template
    /// with no pixel ID). Callers fall back to the direct redirect in that case.</summary>
    public static string Render(PixelSnippet snippet, string? pixelId)
    {
        if (snippet.IsCustom)
            return pixelId ?? "";

        if (string.IsNullOrWhiteSpace(snippet.SnippetTemplate) || string.IsNullOrWhiteSpace(pixelId))
            return "";

        return snippet.SnippetTemplate.Replace(
            PixelSnippetTemplates.Placeholder, pixelId, StringComparison.Ordinal);
    }
}
