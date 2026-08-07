namespace Shortnr.Web.Models;

/// <summary>
/// The five UTM campaign components captured by the Index page's "Advanced"
/// section and stored separately on <see cref="Shortnr.Data.Entities.ShortenedUrlMetadata"/>
/// so the dashboard can filter/report by campaign without parsing the flattened URL.
/// </summary>
public sealed record UtmParameters(
    string? Source,
    string? Medium,
    string? Campaign,
    string? Term,
    string? Content)
{
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Source) &&
        string.IsNullOrWhiteSpace(Medium) &&
        string.IsNullOrWhiteSpace(Campaign) &&
        string.IsNullOrWhiteSpace(Term) &&
        string.IsNullOrWhiteSpace(Content);
}
