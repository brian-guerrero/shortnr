using Shortnr.Data.Entities;

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
    public List<PixelSnippet> PixelSnippets { get; init; } = [];
    public string? ErrorMessage { get; init; }

    /// <summary>True when any advanced field is already populated, so the edit
    /// form's collapsible "Advanced options" section opens expanded instead of
    /// hiding data the link already has.</summary>
    public bool HasAdvancedData =>
        UtmSource.Length > 0 || UtmMedium.Length > 0 || UtmCampaign.Length > 0 ||
        UtmTerm.Length > 0 || UtmContent.Length > 0 || PixelSnippetId is not null ||
        IosDeepLink.Length > 0 || AndroidDeepLink.Length > 0;

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
            PixelSnippets = pixelSnippets ?? [],
            ErrorMessage = errorMessage
        };
    }
}

public class LinkEditSuccessViewModel
{
    public List<ShortenedUrl> Links { get; init; } = [];
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
    public List<ShortenedUrl> Links { get; init; } = [];
    public string Message { get; init; } = "";
}
