using Shortnr.Data.Entities;

namespace Shortnr.Web.Models;

public class DashboardViewModel
{
    public required List<ShortenedUrl> RecentLinks { get; init; }
}
