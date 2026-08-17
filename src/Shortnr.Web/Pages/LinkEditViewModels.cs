using Shortnr.Data.Entities;
using Shortnr.Web.Features.ClickTracking;

namespace Shortnr.Web.Pages;

public class LinkEditViewModel
{
    public long Code { get; init; }
    public string LongUrl { get; init; } = "";
    public string Slug { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string Tags { get; init; } = "";
    public string UtmSource { get; init; } = "";
    public string UtmMedium { get; init; } = "";
    public string UtmCampaign { get; init; } = "";
    public string UtmTerm { get; init; } = "";
    public string UtmContent { get; init; } = "";
    public long? PixelSnippetId { get; init; }
    public bool PixelSnippetIsCustom { get; init; }
    public string PixelId { get; init; } = "";
    public string PixelSnippetHtml { get; init; } = "";
    public string IosDeepLink { get; init; } = "";
    public string AndroidDeepLink { get; init; } = "";
    public string PreviewTheme { get; init; } = "";
    public List<PixelSnippet> PixelSnippets { get; init; } = [];
    public string? ErrorMessage { get; init; }

    /// <summary>True when any advanced field is already populated, so the edit
    /// form's collapsible "Advanced options" section opens expanded instead of
    /// hiding data the link already has.</summary>
    public bool HasAdvancedData =>
        UtmSource.Length > 0 || UtmMedium.Length > 0 || UtmCampaign.Length > 0 ||
        UtmTerm.Length > 0 || UtmContent.Length > 0 || PixelSnippetId is not null ||
        IosDeepLink.Length > 0 || AndroidDeepLink.Length > 0 || PreviewTheme.Length > 0;

    /// <summary>Projection of the advanced-option fields plus the pixel-snippet
    /// catalog into the shared <see cref="LinkAdvancedOptionsViewModel"/> that
    /// backs <c>Shared/_LinkAdvancedOptions.cshtml</c>. The basic Slug field is
    /// already rendered at the top of the edit form, so the partial is told not
    /// to show the "Link identity" custom-code fieldset here.</summary>
    public LinkAdvancedOptionsViewModel AdvancedOptions => new()
    {
        ShowCustomCode = false,
        Slug = Slug,
        UtmSource = UtmSource,
        UtmMedium = UtmMedium,
        UtmCampaign = UtmCampaign,
        UtmTerm = UtmTerm,
        UtmContent = UtmContent,
        PixelSnippetId = PixelSnippetId,
        PixelSnippetIsCustom = PixelSnippetIsCustom,
        PixelId = PixelId,
        PixelSnippetHtml = PixelSnippetHtml,
        IosDeepLink = IosDeepLink,
        AndroidDeepLink = AndroidDeepLink,
        PreviewTheme = PreviewTheme,
        PixelSnippets = PixelSnippets
    };

    public static LinkEditViewModel From(ShortenedUrl link, string? errorMessage = null, List<PixelSnippet>? pixelSnippets = null)
    {
        var metadata = link.Metadata;
        var isCustomPixel = metadata?.PixelSnippet?.IsCustom == true;
        return new()
        {
            Code = link.Id,
            LongUrl = link.LongUrl,
            Slug = link.ShortCode,
            Title = link.Title ?? "",
            Description = link.Description ?? "",
            Tags = string.Join(", ", link.Tags?.Select(t => t.Name) ?? []),
            UtmSource = metadata?.UtmSource ?? "",
            UtmMedium = metadata?.UtmMedium ?? "",
            UtmCampaign = metadata?.UtmCampaign ?? "",
            UtmTerm = metadata?.UtmTerm ?? "",
            UtmContent = metadata?.UtmContent ?? "",
            PixelSnippetId = metadata?.PixelSnippetId,
            PixelSnippetIsCustom = isCustomPixel,
            PixelId = !isCustomPixel ? metadata?.PixelId ?? "" : "",
            PixelSnippetHtml = isCustomPixel ? metadata?.PixelId ?? "" : "",
            IosDeepLink = metadata?.IosDeepLink ?? "",
            AndroidDeepLink = metadata?.AndroidDeepLink ?? "",
            PreviewTheme = link.PreviewTheme ?? "",
            PixelSnippets = pixelSnippets ?? [],
            ErrorMessage = errorMessage
        };
    }
}

/// <summary>Backs the shared <c>Shared/_LinkAdvancedOptions.cshtml</c> partial
/// consumed by both the Index create-form and the Dashboard edit-form's
/// collapsible "Advanced options" section. Form field names are snake_case
/// (e.g. <c>utm_source</c>, <c>pixel_id</c>, <c>ios_deep_link</c>) — the
/// repo-wide convention for multi-word form inputs — so handlers read them
/// via <c>Request.Form["utm_source"]</c>.</summary>
public class LinkAdvancedOptionsViewModel
{
    /// <summary>When true, the partial renders the "Link identity" fieldset
    /// with the optional Custom code (slug) input. The Index create-form sets
    /// this so the user can pick a custom code; the Dashboard edit-form leaves
    /// it false because the Slug is already a required basic field there.</summary>
    public bool ShowCustomCode { get; init; }

    public string Slug { get; init; } = "";
    public string UtmSource { get; init; } = "";
    public string UtmMedium { get; init; } = "";
    public string UtmCampaign { get; init; } = "";
    public string UtmTerm { get; init; } = "";
    public string UtmContent { get; init; } = "";
    public long? PixelSnippetId { get; init; }
    public bool PixelSnippetIsCustom { get; init; }
    public string PixelId { get; init; } = "";
    public string PixelSnippetHtml { get; init; } = "";
    public string IosDeepLink { get; init; } = "";
    public string AndroidDeepLink { get; init; } = "";
    public string PreviewTheme { get; init; } = "";

    public List<PixelSnippet> PixelSnippets { get; init; } = [];

    /// <summary>True when any advanced field is already populated, so the
    /// collapsible section opens expanded instead of hiding data the link
    /// already has. The enclosing form's <c>x-data</c> initialises
    /// <c>showAdvanced</c> from this.</summary>
    public bool HasAdvancedData =>
        UtmSource.Length > 0 || UtmMedium.Length > 0 || UtmCampaign.Length > 0 ||
        UtmTerm.Length > 0 || UtmContent.Length > 0 || PixelSnippetId is not null ||
        IosDeepLink.Length > 0 || AndroidDeepLink.Length > 0 || PreviewTheme.Length > 0;
}

public class LinkEditSuccessViewModel
{
    public List<LinkRowViewModel> Links { get; init; } = [];
    public string Message { get; init; } = "";
}

public class LinkTransferViewModel
{
    public long Code { get; init; }
    public string CurrentWorkspace { get; init; } = "personal";
    public List<Workspace> Workspaces { get; init; } = [];
    public string? ErrorMessage { get; init; }
}

public class LinkTransferSuccessViewModel
{
    public List<LinkRowViewModel> Links { get; init; } = [];
    public string Message { get; init; } = "";
}
