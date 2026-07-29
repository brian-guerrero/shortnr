using System.Security.Claims;
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
    private readonly IConfiguration _config;

    public DashboardModel(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    public async Task<IActionResult> OnGet(string? search, string? linkSort, string? linkDir, string? clickSort, string? clickDir)
    {
        var authEnabled = _config.GetValue<bool>("Authentication:Enabled", defaultValue: true);

        // When auth is on, require a signed-in user. HTMX partial requests get a 401
        // rather than a redirect so the browser isn't transparently swapped to the login page.
        if (authEnabled && User.Identity?.IsAuthenticated != true)
        {
            if (Request.Headers["HX-Request"].Count > 0)
                return Unauthorized();

            return RedirectToPage("/Index", new { returnUrl = "/dashboard" });
        }

        // Resolve the current user's DB row id so we can scope every query.
        // Null means auth is disabled — show all records.
        var ownerUserId = authEnabled ? await ResolveOwnerUserIdAsync() : null;

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
                var clicks = await query.Take(20).ToListAsync();

                return Partial("Shared/_RecentClicks", clicks);
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
            var results = await linkQ.Take(50).ToListAsync();

            return Partial("Shared/_SearchResults", results);
        }

        return Page();
    }

    // Mirrors IndexModel.ResolveOwnerUserIdAsync — looks up the Users row by OIDC
    // (Issuer, Subject). Returns null if the row hasn't been provisioned yet (narrow
    // race on first login) or if the principal carries no subject claim.
    private async Task<long?> ResolveOwnerUserIdAsync()
    {
        var subject = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (subject is null) return null;

        var issuer = _config["Authentication:Oidc:Authority"] ?? string.Empty;
        var owner = await _db.Users.FirstOrDefaultAsync(u => u.Issuer == issuer && u.Subject == subject);
        return owner?.Id;
    }
}
