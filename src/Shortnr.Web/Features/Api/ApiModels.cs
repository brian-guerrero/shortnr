namespace Shortnr.Web.Features.Api;

/// <summary>POST /api/v1/links request body.</summary>
public record CreateLinkRequest
{
    public required string Url { get; init; }
    public string? Slug { get; init; }
    public string? Domain { get; init; }
    public string? Workspace { get; init; }
}

/// <summary>PUT /api/v1/links/{shortCode} request body. Omitted fields keep their current value.</summary>
public record UpdateLinkRequest
{
    public string? Url { get; init; }
    public string? Slug { get; init; }
    public string? Domain { get; init; }
    public string? Workspace { get; init; }
}

public record LinkResponse(
    string ShortCode,
    string ShortUrl,
    string LongUrl,
    string? Domain,
    long ClickCount,
    DateTime CreatedAtUtc,
    string? Workspace = null);

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
