using System.Text.Json.Serialization;

namespace Shortnr.Cli.Models;

public record CreateLinkRequest(
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("slug")] string? Slug = null,
    [property: JsonPropertyName("domain")] string? Domain = null);

public record LinkResponse(
    [property: JsonPropertyName("shortCode")] string ShortCode,
    [property: JsonPropertyName("shortUrl")] string ShortUrl,
    [property: JsonPropertyName("longUrl")] string LongUrl,
    [property: JsonPropertyName("domain")] string? Domain,
    [property: JsonPropertyName("clickCount")] long ClickCount,
    [property: JsonPropertyName("createdAtUtc")] DateTime CreatedAtUtc,
    [property: JsonPropertyName("workspace")] string? Workspace = null);

public record LinkListResponse(
    [property: JsonPropertyName("links")] IReadOnlyList<LinkResponse> Links,
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("pageSize")] int PageSize,
    [property: JsonPropertyName("total")] long Total);

public record ClickListResponse(
    [property: JsonPropertyName("clicks")] IReadOnlyList<ClickRow> Clicks,
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("pageSize")] int PageSize,
    [property: JsonPropertyName("total")] long Total);

public record ClickRow(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("shortCode")] string ShortCode,
    [property: JsonPropertyName("countryCode")] string? CountryCode,
    [property: JsonPropertyName("countryName")] string? CountryName,
    [property: JsonPropertyName("cityName")] string? CityName,
    [property: JsonPropertyName("browser")] string? Browser,
    [property: JsonPropertyName("browserVersion")] string? BrowserVersion,
    [property: JsonPropertyName("operatingSystem")] string? OperatingSystem,
    [property: JsonPropertyName("osVersion")] string? OSVersion,
    [property: JsonPropertyName("referer")] string? Referer,
    [property: JsonPropertyName("ipAddress")] string? IpAddress,
    [property: JsonPropertyName("deviceFamily")] string? DeviceFamily,
    [property: JsonPropertyName("clickedAtUtc")] DateTime ClickedAtUtc);

public record ErrorResponse(
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("status")] int? Status,
    [property: JsonPropertyName("errors")] Dictionary<string, string[]>? Errors);
