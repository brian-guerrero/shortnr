namespace Shortnr.Web.Features.Api;

/// <summary>
/// Campaign metadata nested on create/update requests: UTM params, a retargeting
/// pixel snippet (selected by name — see GET /api/v1/pixel-snippets), and platform
/// deep links. On update, each field independently follows the same
/// omit-keeps/empty-string-clears convention as the top-level request fields
/// (unlike the top-level request, this means a request can touch just one
/// metadata field without resending the others).
/// </summary>
public record LinkMetadataRequest
{
    public string? UtmSource { get; init; }
    public string? UtmMedium { get; init; }
    public string? UtmCampaign { get; init; }
    public string? UtmTerm { get; init; }
    public string? UtmContent { get; init; }

    /// <summary>Name of a pixel snippet from GET /api/v1/pixel-snippets. Empty string
    /// removes the pixel; omit to leave the current selection untouched.</summary>
    public string? PixelSnippet { get; init; }

    /// <summary>Pixel ID substituted into a template snippet. Required (here or from
    /// the link's current value) when PixelSnippet names a template, non-custom snippet.</summary>
    public string? PixelId { get; init; }

    /// <summary>Full custom snippet HTML. Required (here or from the link's current
    /// value) when PixelSnippet names the custom snippet.</summary>
    public string? PixelSnippetHtml { get; init; }

    public string? IosDeepLink { get; init; }
    public string? AndroidDeepLink { get; init; }
}

public record LinkMetadataResponse(
    string? UtmSource, string? UtmMedium, string? UtmCampaign, string? UtmTerm, string? UtmContent,
    string? PixelSnippet, string? PixelValue, string? IosDeepLink, string? AndroidDeepLink);

/// <summary>GET /api/v1/pixel-snippets response row.</summary>
public record PixelSnippetResponse(string Name, bool IsCustom);

/// <summary>POST /api/v1/links request body.</summary>
public record CreateLinkRequest
{
    public required string Url { get; init; }
    public string? Slug { get; init; }
    public string? Domain { get; init; }
    public string? Workspace { get; init; }
    public LinkMetadataRequest? Metadata { get; init; }
}

/// <summary>PUT /api/v1/links/{shortCode} request body. Omitted fields keep their current value.</summary>
public record UpdateLinkRequest
{
    public string? Url { get; init; }
    public string? Slug { get; init; }
    public string? Domain { get; init; }
    public string? Workspace { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }
    public LinkMetadataRequest? Metadata { get; init; }
}

/// <summary>POST /api/v1/links/{shortCode}/transfer request body.</summary>
public record TransferLinkRequest
{
    public required string Workspace { get; init; }
}

public record LinkResponse(
    string ShortCode,
    string ShortUrl,
    string LongUrl,
    string? Domain,
    long ClickCount,
    DateTime CreatedAtUtc,
    string? Workspace = null,
    IReadOnlyList<string>? Tags = null,
    string? Title = null,
    string? Description = null,
    DateTime? ArchivedAtUtc = null,
    DateTime? UpdatedAtUtc = null,
    LinkMetadataResponse? Metadata = null);

public record LinkListResponse(
    IReadOnlyList<LinkResponse> Links,
    int Page,
    int PageSize,
    long Total);

public record ClickListResponse(
    IReadOnlyList<ApiClickRow> Clicks,
    int Page,
    int PageSize,
    long Total);

/// <summary>Click-event row shape exposed by the API (mirrors the dashboard's ClickEventRow fields).</summary>
public record ApiClickRow(
    long Id,
    string ShortCode,
    string? CountryCode,
    string? CountryName,
    string? CityName,
    string? Browser,
    string? BrowserVersion,
    string? OperatingSystem,
    string? OSVersion,
    string? Referer,
    string? IpAddress,
    string? DeviceFamily,
    DateTime ClickedAtUtc);
