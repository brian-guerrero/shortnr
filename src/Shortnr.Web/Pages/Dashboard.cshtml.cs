using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Web.Models;
using Shortnr.Web.Services;

namespace Shortnr.Web.Pages;

public class DashboardModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly UserIdentityService _identity;

    public DashboardModel(AppDbContext db, UserIdentityService identity)
    {
        _db = db;
        _identity = identity;
    }

    public async Task<IActionResult> OnGet(string? search, string? linkSort, string? linkDir, string? clickSort, string? clickDir, int? clickLimit)
    {
        if (_identity.IsAuthEnabled && User.Identity?.IsAuthenticated != true)
        {
            if (Request.Headers["HX-Request"].Count > 0)
                return Unauthorized();

            return RedirectToPage("/Index");
        }

        var ownerUserId = await _identity.ResolveOwnerUserIdAsync(User);

        if (Request.Headers["HX-Request"].Count > 0)
        {
            var target = Request.Headers["HX-Target"].FirstOrDefault();

            if (target == "recent-clicks")
            {
                var query = _db.ClickEvents.Include(e => e.ShortenedUrl).AsQueryable();
                if (ownerUserId is not null)
                    query = query.Where(e => e.ShortenedUrl.OwnerUserId == ownerUserId);

                query = (clickSort, clickDir == "desc") switch
                {
                    ("shortCode", false) => query.OrderBy(e => e.ShortenedUrl.ShortCode),
                    ("shortCode", true) => query.OrderByDescending(e => e.ShortenedUrl.ShortCode),
                    ("countryCode", false) => query.OrderBy(e => e.CountryCode ?? ""),
                    ("countryCode", true) => query.OrderByDescending(e => e.CountryCode ?? ""),
                    ("browser", false) => query.OrderBy(e => e.Browser ?? ""),
                    ("browser", true) => query.OrderByDescending(e => e.Browser ?? ""),
                    ("operatingSystem", false) => query.OrderBy(e => e.OperatingSystem ?? ""),
                    ("operatingSystem", true) => query.OrderByDescending(e => e.OperatingSystem ?? ""),
                    ("referer", false) => query.OrderBy(e => e.Referer),
                    ("referer", true) => query.OrderByDescending(e => e.Referer),
                    ("clickedAtUtc", false) => query.OrderBy(e => e.ClickedAtUtc),
                    ("clickedAtUtc", true) => query.OrderByDescending(e => e.ClickedAtUtc),
                    _ => query.OrderByDescending(e => e.ClickedAtUtc)
                };

                var limit = clickLimit is >= 5 and <= 20 ? clickLimit.Value : 5;
                return Partial("Shared/_RecentClicks", await query.Take(limit).ToListAsync());
            }

            // Combined dashboard-data target: metrics + geo breakdown + chart JSON.
            // Uses a single links query and a single grouped geo query (fixes N+1).
            var linkQuery = _db.ShortenedUrls.AsQueryable();
            if (ownerUserId is not null)
                linkQuery = linkQuery.Where(l => l.OwnerUserId == ownerUserId);

            var links = await linkQuery
                .Select(l => new { l.ShortCode, l.ClickCount })
                .ToListAsync();

            var totalLinks = links.Count;
            var totalClicks = links.Sum(l => (long)l.ClickCount);
            var topLinks = links
                .OrderByDescending(l => l.ClickCount)
                .Take(10)
                .Select(l => new { shortCode = l.ShortCode, clickCount = l.ClickCount })
                .ToList();

            var clickQuery = _db.ClickEvents.AsQueryable();
            if (ownerUserId is not null)
                clickQuery = clickQuery.Where(e => e.ShortenedUrl.OwnerUserId == ownerUserId);

            var totalCountries = await clickQuery
                .Where(e => e.CountryCode != null && e.CountryCode != "")
                .Select(e => e.CountryCode)
                .Distinct()
                .CountAsync();

            var geoRows = await clickQuery
                .Where(e => e.CountryCode != null && e.CountryCode != "")
                .GroupBy(e => new { e.CountryCode, e.CountryName, CityName = e.CityName ?? "" })
                .Select(g => new { g.Key.CountryCode, g.Key.CountryName, g.Key.CityName, Count = g.Count() })
                .ToListAsync();

            var geoBreakdown = geoRows
                .GroupBy(g => new { g.CountryCode, g.CountryName })
                .Select(g => new GeoBreakdownItem
                {
                    CountryCode = g.Key.CountryCode ?? "",
                    CountryName = g.Key.CountryName ?? "",
                    TotalClicks = g.Sum(x => x.Count),
                    CityCounts = g
                        .Where(x => !string.IsNullOrEmpty(x.CityName))
                        .OrderByDescending(x => x.Count)
                        .Take(5)
                        .Select(x => new CityCount { City = x.CityName, Count = x.Count })
                        .ToList()
                })
                .OrderByDescending(x => x.TotalClicks)
                .Take(10)
                .ToList();

            var countryChartData = geoBreakdown
                .Select(g => new { countryCode = g.CountryCode, count = g.TotalClicks })
                .ToList();

            var chartJson = JsonSerializer.Serialize(new
            {
                topLinks,
                countryBreakdown = countryChartData
            });

            return Partial("Shared/_DashboardData", new DashboardDataViewModel
            {
                TotalLinks = totalLinks,
                TotalClicks = totalClicks,
                TotalCountries = totalCountries,
                ChartJson = chartJson,
                GeoBreakdown = geoBreakdown
            });
        }

        return Page();
    }
}