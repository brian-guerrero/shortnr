namespace Shortnr.Web.Models;

public class ClickRecord
{
    public required string ShortCode { get; init; }
    public string IpAddress { get; init; } = string.Empty;
    public string UserAgent { get; init; } = string.Empty;
    public string Referer { get; init; } = string.Empty;
}
