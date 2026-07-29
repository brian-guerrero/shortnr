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

    public async Task<IActionResult> OnGet(string? search, string? linkSort, string? linkDir, string? clickSort, string? clickDir)
    {
        // When auth is on, require a signed-in user. HTMX partial requests get a 401
        // rather than a redirect so the browser isn't transparently swapped to the login page.
        if (_identity.IsAuthEnabled && User.Identity?.IsAuthenticated != true)
        {
            if (Request.Headers["HX-Request"].Count > 0)
                return Unauthorized();

            return RedirectToPage("/Index");
        }

        // Null when auth is disabled — queries run unfiltered.
        var ownerUserId = await _identity.ResolveOwnerUserIdAsync(User);

        if (Request.Headers["HX-Request"].Count > 0)
        {
            var target = Request.Headers["HX-Target"].FirstOrDefault();

            if (target == "metrics-summary")
            {
                var linkQuery = _db.ShortenedUrls.AsQueryable();
                if (ownerUserId is not null)
                    linkQuery = linkQuery.Where(l => l.OwnerUserId == ownerUserId);

                var totalLinks = await linkQuery.CountAsync();
                var totalClicks = await linkQuery.SumAsync(l => (long?)l.ClickCount) ?? 0;

                return Partial("Shared/_DashboardMetrics", new DashboardMetricsViewModel
                {
                    TotalLinks = totalLinks,
                    TotalClicks = totalClicks
                });
            }

            if (target == "recent-clicks")
            {
                var query = _db.ClickEvents.Include(e => e.ShortenedUrl).AsQueryable();
                if (ownerUserId is not null)
                    query = query.Where(e => e.ShortenedUrl.OwnerUserId == ownerUserId);

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

                return Partial("Shared/_RecentClicks", await query.Take(20).ToListAsync());
            }

            // Search / link list
            var linkQ = _db.ShortenedUrls.AsQueryable();
            if (ownerUserId is not null)
                linkQ = linkQ.Where(l => l.OwnerUserId == ownerUserId);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var lower = search.ToLower();
                linkQ = linkQ.Where(l => l.LongUrl.ToLower().Contains(lower) || l.ShortCode.ToLower().Contains(lower));
            }
            linkQ = (linkSort, linkDir == "desc") switch
            {
                ("shortCode", false) => linkQ.OrderBy(l => l.ShortCode),
                ("shortCode", true) => linkQ.OrderByDescending(l => l.ShortCode),
                ("longUrl", false) => linkQ.OrderBy(l => l.LongUrl),
                ("longUrl", true) => linkQ.OrderByDescending(l => l.LongUrl),
                ("clickCount", false) => linkQ.OrderBy(l => l.ClickCount),
                ("clickCount", true) => linkQ.OrderByDescending(l => l.ClickCount),
                ("createdAtUtc", false) => linkQ.OrderBy(l => l.CreatedAtUtc),
                ("createdAtUtc", true) => linkQ.OrderByDescending(l => l.CreatedAtUtc),
                _ => linkQ.OrderByDescending(l => l.CreatedAtUtc)
            };

            return Partial("Shared/_SearchResults", await linkQ.Take(50).ToListAsync());
        }

        return Page();
    }
}
