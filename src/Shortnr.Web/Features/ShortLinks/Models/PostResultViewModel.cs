using Shortnr.Data.Entities;

namespace Shortnr.Web.Features.ShortLinks.Models;

public class PostResultViewModel
{
    public string? ShortUrl { get; init; }
    public string? ShortCode { get; init; }
    public bool HasError { get; init; }
    public string? ErrorMessage { get; init; }
    public required List<ShortenedUrl> RecentLinks { get; init; }
}
