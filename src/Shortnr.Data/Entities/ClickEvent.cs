namespace Shortnr.Data.Entities;

public class ClickEvent
{
    public long Id { get; set; }
    public long ShortenedUrlId { get; set; }
    public string IpAddress { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
    public string Referer { get; set; } = string.Empty;
    public DateTime ClickedAtUtc { get; set; }

    public string? CountryCode { get; set; }
    public string? CountryName { get; set; }
    public string? CityName { get; set; }
    public string? PostalCode { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    public string? DeviceFamily { get; set; }
    public string? OperatingSystem { get; set; }
    public string? OSVersion { get; set; }
    public string? Browser { get; set; }
    public string? BrowserVersion { get; set; }

    public ShortenedUrl ShortenedUrl { get; set; } = null!;
}
