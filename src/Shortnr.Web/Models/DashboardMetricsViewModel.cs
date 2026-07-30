namespace Shortnr.Web.Models;

public class DashboardMetricsViewModel
{
    public long TotalLinks { get; init; }
    public long TotalClicks { get; init; }
    public int TotalCountries { get; init; }
}

public class GeoBreakdownItem
{
    public string CountryCode { get; init; } = "";
    public string CountryName { get; init; } = "";
    public List<CityCount> CityCounts { get; init; } = [];
    public int TotalClicks { get; init; }
}

public class CityCount
{
    public string City { get; init; } = "";
    public int Count { get; init; }
}
