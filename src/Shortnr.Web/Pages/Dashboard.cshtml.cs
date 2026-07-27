using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Web.Models;

namespace Shortnr.Web.Pages;

public class DashboardModel : PageModel
{
    private readonly AppDbContext _db;

    public DashboardModel(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> OnGet(string? search, string? linkSort, string? linkDir, string? clickSort, string? clickDir)
    {
        if (Request.Headers["HX-Request"].Count > 0)
        {
            var target = Request.Headers["HX-Target"].FirstOrDefault();

            if (target == "metrics-summary")
            {
                var totalLinks = await _db.ShortenedUrls.CountAsync();
                var totalClicks = await _db.ShortenedUrls.SumAsync(l => (long?)l.ClickCount) ?? 0;

                return Partial("Shared/_DashboardMetrics", new DashboardMetricsViewModel
                {
                    TotalLinks = totalLinks,
                    TotalClicks = totalClicks
                });
            }

            if (target == "recent-clicks")
            {
                var query = _db.ClickEvents.Include(e => e.ShortenedUrl).AsQueryable();
                query = (clickSort, clickDir == "desc") switch
                {
                    ("shortCode", false) => query.OrderBy(e => e.ShortenedUrl.ShortCode),
                    ("shortCode", true) => query.OrderByDescending(e => e.ShortenedUrl.ShortCode),
                    ("ipAddress", false) => query.OrderBy(e => e.IpAddress),
                    ("ipAddress", true) => query.OrderByDescending(e => e.IpAddress),
                    ("referer", false) => query.OrderBy(e => e.Referer),
                    ("referer", true) => query.OrderByDescending(e => e.Referer),
                    ("userAgent", false) => query.OrderBy(e => e.UserAgent),
                    ("userAgent", true) => query.OrderByDescending(e => e.UserAgent),
                    ("clickedAtUtc", false) => query.OrderBy(e => e.ClickedAtUtc),
                    ("clickedAtUtc", true) => query.OrderByDescending(e => e.ClickedAtUtc),
                    _ => query.OrderByDescending(e => e.ClickedAtUtc)
                };
                var clicks = await query.Take(20).ToListAsync();

                return Partial("Shared/_RecentClicks", clicks);
            }

            var linkQuery = _db.ShortenedUrls.AsQueryable();
            if (!string.IsNullOrWhiteSpace(search))
            {
                var lower = search.ToLower();
                linkQuery = linkQuery.Where(l => l.LongUrl.ToLower().Contains(lower) || l.ShortCode.ToLower().Contains(lower));
            }
            linkQuery = (linkSort, linkDir == "desc") switch
            {
                ("shortCode", false) => linkQuery.OrderBy(l => l.ShortCode),
                ("shortCode", true) => linkQuery.OrderByDescending(l => l.ShortCode),
                ("longUrl", false) => linkQuery.OrderBy(l => l.LongUrl),
                ("longUrl", true) => linkQuery.OrderByDescending(l => l.LongUrl),
                ("clickCount", false) => linkQuery.OrderBy(l => l.ClickCount),
                ("clickCount", true) => linkQuery.OrderByDescending(l => l.ClickCount),
                ("createdAtUtc", false) => linkQuery.OrderBy(l => l.CreatedAtUtc),
                ("createdAtUtc", true) => linkQuery.OrderByDescending(l => l.CreatedAtUtc),
                _ => linkQuery.OrderByDescending(l => l.CreatedAtUtc)
            };
            var results = await linkQuery.Take(50).ToListAsync();

            return Partial("Shared/_SearchResults", results);
        }

        return Page();
    }
}
