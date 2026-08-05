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

    public List<string> DomainOptions { get; set; } = [];
    public ActiveWorkspaceContext? Workspace { get; set; }

    public async Task<IActionResult> OnGet(string? search, string? linkSort, string? linkDir, string? clickSort, string? clickDir, int? clickLimit, string? domain)
    {
        if (_identity.IsAuthEnabled && User.Identity?.IsAuthenticated != true)
        {
            if (Request.Headers["HX-Request"].Count > 0)
                return Unauthorized();

            return RedirectToPage("/Index");
        }

        var ownerUserId = await _identity.ResolveOwnerUserIdAsync(User);
        Workspace = await _identity.ResolveActiveWorkspaceContextAsync(User);
        var workspaceId = Workspace?.WorkspaceId;

        if (Request.Headers["HX-Request"].Count > 0)
        {
            var target = Request.Headers["HX-Target"].FirstOrDefault();

            if (target == "recent-clicks")
            {
                var query = ApplyClickScoping(_db.ClickEvents.AsQueryable(), ownerUserId, workspaceId);

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
                return Partial("Shared/_RecentClicks", await LoadRecentClicksAsync(query, limit));
            }

            if (target == "search-results")
            {
                var linkQ = ApplyScoping(_db.ShortenedUrls.AsQueryable(), ownerUserId, workspaceId);

                if (!string.IsNullOrWhiteSpace(search))
                {
                    var lower = search.ToLower();
                    linkQ = linkQ.Where(l => l.LongUrl.ToLower().Contains(lower)
                        || l.ShortCode.ToLower().Contains(lower)
                        || (l.Domain != null && l.Domain.Hostname.ToLower().Contains(lower)));
                }
                if (!string.IsNullOrEmpty(domain))
                {
                    linkQ = domain == "default"
                        ? linkQ.Where(l => l.DomainId == null)
                        : linkQ.Where(l => l.Domain != null && l.Domain.Hostname == domain);
                }
                linkQ = (linkSort, linkDir == "desc") switch
                {
                    ("shortCode", false) => linkQ.OrderBy(l => l.ShortCode),
                    ("shortCode", true) => linkQ.OrderByDescending(l => l.ShortCode),
                    ("domain", false) => linkQ.OrderBy(l => l.Domain == null ? "" : l.Domain.Hostname).ThenBy(l => l.ShortCode),
                    ("domain", true) => linkQ.OrderByDescending(l => l.Domain == null ? "" : l.Domain.Hostname).ThenBy(l => l.ShortCode),
                    ("longUrl", false) => linkQ.OrderBy(l => l.LongUrl),
                    ("longUrl", true) => linkQ.OrderByDescending(l => l.LongUrl),
                    ("clickCount", false) => linkQ.OrderBy(l => l.ClickCount),
                    ("clickCount", true) => linkQ.OrderByDescending(l => l.ClickCount),
                    ("createdAtUtc", false) => linkQ.OrderBy(l => l.CreatedAtUtc),
                    ("createdAtUtc", true) => linkQ.OrderByDescending(l => l.CreatedAtUtc),
                    _ => linkQ.OrderByDescending(l => l.CreatedAtUtc)
                };

                return Partial("Shared/_SearchResults", await linkQ.Take(50).Include(l => l.Domain).Include(l => l.Workspace).ToListAsync());
            }

            var linkQuery = ApplyScoping(_db.ShortenedUrls.AsQueryable(), ownerUserId, workspaceId);

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

            var clickQuery = ApplyClickScoping(_db.ClickEvents.AsQueryable(), ownerUserId, workspaceId);

            var geoRows = await clickQuery
                .Where(e => e.CountryCode != null && e.CountryCode != "")
                .GroupBy(e => new { e.CountryCode, e.CountryName, CityName = e.CityName ?? "" })
                .Select(g => new { g.Key.CountryCode, g.Key.CountryName, g.Key.CityName, Count = g.Count() })
                .ToListAsync();

            var totalCountries = geoRows
                .Select(g => g.CountryCode)
                .Where(cc => !string.IsNullOrEmpty(cc))
                .Distinct()
                .Count();

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

            var recentClicksQuery = ApplyClickScoping(_db.ClickEvents.AsQueryable(), ownerUserId, workspaceId);
            recentClicksQuery = recentClicksQuery.OrderByDescending(e => e.ClickedAtUtc);
            var clickLimitValue = clickLimit is >= 5 and <= 20 ? clickLimit.Value : 5;
            var recentClicks = await LoadRecentClicksAsync(recentClicksQuery, clickLimitValue);

            return Partial("Shared/_DashboardData", new DashboardDataViewModel
            {
                TotalLinks = totalLinks,
                TotalClicks = totalClicks,
                TotalCountries = totalCountries,
                ChartJson = chartJson,
                GeoBreakdown = geoBreakdown,
                RecentClicks = recentClicks
            });
        }

        DomainOptions = await LoadDomainOptionsAsync();
        return Page();
    }

    private static IQueryable<ShortenedUrl> ApplyScoping(IQueryable<ShortenedUrl> query, long? ownerUserId, long? workspaceId)
    {
        if (workspaceId is not null)
            return query.Where(l => l.WorkspaceId == workspaceId);
        if (ownerUserId is not null)
            return query.Where(l => l.OwnerUserId == ownerUserId);
        return query;
    }

    private static IQueryable<ClickEvent> ApplyClickScoping(IQueryable<ClickEvent> query, long? ownerUserId, long? workspaceId)
    {
        if (workspaceId is not null)
            return query.Where(e => e.ShortenedUrl.WorkspaceId == workspaceId);
        if (ownerUserId is not null)
            return query.Where(e => e.ShortenedUrl.OwnerUserId == ownerUserId);
        return query;
    }

    private async Task<List<string>> LoadDomainOptionsAsync()
    {
        var ownerUserId = await _identity.ResolveOwnerUserIdAsync(User);
        var query = _db.Domains.AsQueryable();
        if (ownerUserId is not null)
            query = query.Where(d => d.OwnerUserId == ownerUserId);

        var hostnames = await query.OrderBy(d => d.Hostname).Select(d => d.Hostname).ToListAsync();
        return new List<string> { "default" }.Concat(hostnames).ToList();
    }

    private static async Task<List<ClickEventRow>> LoadRecentClicksAsync(IQueryable<ClickEvent> query, int limit)
    {
        return await query
            .Take(limit)
            .Select(e => new ClickEventRow
            {
                Id = e.Id,
                ShortCode = e.ShortenedUrl.ShortCode,
                Hostname = e.ShortenedUrl.Domain == null ? null : e.ShortenedUrl.Domain.Hostname,
                CountryCode = e.CountryCode,
                Browser = e.Browser,
                BrowserVersion = e.BrowserVersion,
                OperatingSystem = e.OperatingSystem,
                OSVersion = e.OSVersion,
                Referer = e.Referer,
                ClickedAtUtc = e.ClickedAtUtc,
                IpAddress = e.IpAddress,
                UserAgent = e.UserAgent,
                DeviceFamily = e.DeviceFamily,
                CityName = e.CityName
            })
            .ToListAsync();
    }
}
