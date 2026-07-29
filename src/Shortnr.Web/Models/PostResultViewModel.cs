using Shortnr.Data.Entities;

namespace Shortnr.Web.Models;

public class PostResultViewModel
{
    public required string ShortUrl { get; init; }
    public required string ShortCode { get; init; }
    public required List<ShortenedUrl> RecentLinks { get; init; }
}
