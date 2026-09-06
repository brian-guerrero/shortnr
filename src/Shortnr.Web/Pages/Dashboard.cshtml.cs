using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;

namespace Shortnr.Web.Pages;

public class DashboardModel(AppDbContext db, UserIdentityService identity)
    : AuthorizedPageModel(identity)
{
    public List<string> DomainOptions { get; set; } = [];
    public ActiveWorkspaceContext? Workspace { get; set; }

    public async Task<IActionResult> OnGet(string? search, string? linkSort, string? linkDir, string? clickSort, string? clickDir, int? clickLimit, string? domain)
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        var ownerUserId = await ResolveOwnerUserIdAsync();
        Workspace = await ResolveWorkspaceAsync();
        var workspaceId = Workspace?.WorkspaceId;

        if (IsHtmxRequest)
        {
            var target = Request.Headers["HX-Target"].FirstOrDefault();

            if (target == "recent-clicks")
            {
                var query = db.ClickEvents.ScopeToOwner(ownerUserId, workspaceId);

                var clickDesc = clickDir == "desc";
                query = clickSort switch
                {
                    "shortCode" => Sort(query, e => e.ShortenedUrl.ShortCode, clickDesc),
                    "countryCode" => Sort(query, e => e.CountryCode ?? "", clickDesc),
                    "browser" => Sort(query, e => e.Browser ?? "", clickDesc),
                    "operatingSystem" => Sort(query, e => e.OperatingSystem ?? "", clickDesc),
                    "referer" => Sort(query, e => e.Referer, clickDesc),
                    "clickedAtUtc" => Sort(query, e => e.ClickedAtUtc, clickDesc),
                    _ => query.OrderByDescending(e => e.ClickedAtUtc)
                };

                var limit = clickLimit is >= 5 and <= 20 ? clickLimit.Value : 5;
                return Partial("Shared/_RecentClicks", await LoadRecentClicksAsync(query, limit));
            }

            if (target == "search-results")
            {
                var linkQ = db.ShortenedUrls.ScopeToOwner(ownerUserId, workspaceId);

                if (!string.IsNullOrWhiteSpace(search))
                {
                    var lower = search.ToLowerInvariant();
                    linkQ = linkQ.Where(l => l.LongUrl.ToLower().Contains(lower)
                        || l.ShortCode.ToLower().Contains(lower)
                        || (l.Domain != null && l.Domain.Hostname.ToLower().Contains(lower)));
                }
                linkQ = linkQ.FilterByDomain(domain);
                var linkDesc = linkDir == "desc";
                linkQ = linkSort switch
                {
                    "shortCode" => Sort(linkQ, l => l.ShortCode, linkDesc),
                    // Equal hostnames stay in short-code order.
                    "domain" => Sort(linkQ, l => l.Domain == null ? "" : l.Domain.Hostname, linkDesc).ThenBy(l => l.ShortCode),
                    "longUrl" => Sort(linkQ, l => l.LongUrl, linkDesc),
                    "clickCount" => Sort(linkQ, l => l.ClickCount, linkDesc),
                    "createdAtUtc" => Sort(linkQ, l => l.CreatedAtUtc, linkDesc),
                    _ => linkQ.OrderByDescending(l => l.CreatedAtUtc)
                };

                return Partial("Shared/_SearchResults", await linkQ
                    .AsNoTracking()
                    .Include(l => l.Domain)
                    .Include(l => l.Workspace)
                    .Take(50)
                    .ToListAsync());
            }

            var linkQuery = db.ShortenedUrls.ScopeToOwner(ownerUserId, workspaceId);

            // Aggregate in SQL rather than pulling the whole link table into memory —
            // this branch runs on every dashboard poll.
            var totalLinks = await linkQuery.CountAsync();
            var totalClicks = await linkQuery.SumAsync(l => (long?)l.ClickCount) ?? 0;
            var topLinks = await linkQuery
                .OrderByDescending(l => l.ClickCount)
                .Take(10)
                .Select(l => new { shortCode = l.ShortCode, clickCount = l.ClickCount })
                .ToListAsync();

            var clickQuery = db.ClickEvents.ScopeToOwner(ownerUserId, workspaceId);

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

            var recentClicksQuery = db.ClickEvents
                .ScopeToOwner(ownerUserId, workspaceId)
                .OrderByDescending(e => e.ClickedAtUtc);
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

    /// <summary>
    /// Generic so the key stays strongly typed — an <c>Expression&lt;Func&lt;T, object&gt;&gt;</c>
    /// would box the key and change the emitted ORDER BY.
    /// </summary>
    private static IOrderedQueryable<T> Sort<T, TKey>(IQueryable<T> query, Expression<Func<T, TKey>> key, bool descending) =>
        descending ? query.OrderByDescending(key) : query.OrderBy(key);

    private async Task<List<string>> LoadDomainOptionsAsync()
    {
        var ownerUserId = await ResolveOwnerUserIdAsync();
        var hostnames = await db.Domains
            .ScopeToOwner(ownerUserId, workspaceId: null)
            .OrderBy(d => d.Hostname)
            .Select(d => d.Hostname)
            .ToListAsync();
        var options = new List<string>(hostnames.Count + 1) { "default" };
        options.AddRange(hostnames);
        return options;
    }

    private static async Task<List<ClickEventRow>> LoadRecentClicksAsync(IQueryable<ClickEvent> query, int limit)
    {
        return await query
            .Take(limit)
            .Select(e => new ClickEventRow
            {
                Id = e.Id,
                ShortCode = e.ShortenedUrl.ShortCode,
                Hostname = e.ShortenedUrl.Domain!.Hostname,
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