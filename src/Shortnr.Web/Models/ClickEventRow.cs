namespace Shortnr.Web.Models;

public class ClickEventRow
{
    public long Id { get; init; }
    public string ShortCode { get; init; } = "";
    public string? CountryCode { get; init; }
    public string? Browser { get; init; }
    public string? BrowserVersion { get; init; }
    public string? OperatingSystem { get; init; }
    public string? OSVersion { get; init; }
    public string Referer { get; init; } = "";
    public DateTime ClickedAtUtc { get; init; }
    public string IpAddress { get; init; } = "";
    public string UserAgent { get; init; } = "";
    public string? DeviceFamily { get; init; }
    public string? CityName { get; init; }
}
