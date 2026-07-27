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

    public async Task<IActionResult> OnGet(string? search)
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

            var query = _db.ShortenedUrls.AsQueryable();
            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(l => l.LongUrl.ToLower().Contains(search.ToLower()) || l.ShortCode.ToLower().Contains(search.ToLower()));
            }
            var results = await query
                .OrderByDescending(l => l.CreatedAtUtc)
                .Take(50)
                .ToListAsync();

            return Partial("Shared/_SearchResults", results);
        }

        return Page();
    }
}
