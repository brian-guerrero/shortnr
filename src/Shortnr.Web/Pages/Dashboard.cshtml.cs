using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Web.Features.Workspaces;

namespace Shortnr.Web.Pages;

public class DashboardModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly UserIdentityService _identity;
    private readonly WorkspaceService _workspaces;

    public DashboardModel(AppDbContext db, UserIdentityService identity, WorkspaceService workspaces)
    {
        _db = db;
        _identity = identity;
        _workspaces = workspaces;
    }

    public List<string> DomainOptions { get; set; } = [];
    public ActiveWorkspaceContext? Workspace { get; set; }
    public string? StatusMessage { get; set; }
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGet(string? search, string? linkSort, string? linkDir, string? clickSort, string? clickDir, int? clickLimit, string? domain, string? status)
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

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
                return Partial("Shared/_SearchResults", await LoadLinksAsync(search, linkSort, linkDir, domain, status));

            var linkQuery = ApplyScoping(_db.ShortenedUrls.AsQueryable(), ownerUserId, workspaceId);

            // Aggregate in SQL rather than pulling the whole link table into memory —
            // this branch runs on every dashboard poll.
            var totalLinks = await linkQuery.CountAsync();
            var totalClicks = await linkQuery.SumAsync(l => (long?)l.ClickCount) ?? 0;
            var topLinks = await linkQuery
                .OrderByDescending(l => l.ClickCount)
                .Take(10)
                .Select(l => new { shortCode = l.ShortCode, clickCount = l.ClickCount })
                .ToListAsync();

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

    public async Task<IActionResult> OnGetEdit(long? code)
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        Workspace = await _identity.ResolveActiveWorkspaceContextAsync(User);

        if (code is null)
            return Partial("Shared/_LinkEditForm", new LinkEditViewModel { ErrorMessage = "Link not found." });

        var link = await FindLinkAsync(code.Value);
        if (link is null)
            return Partial("Shared/_LinkEditForm", new LinkEditViewModel { ErrorMessage = "Link not found." });

        return Partial("Shared/_LinkEditForm", LinkEditViewModel.From(link));
    }

    public async Task<IActionResult> OnGetTransfer(long? code)
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        Workspace = await _identity.ResolveActiveWorkspaceContextAsync(User);

        if (code is null)
            return Partial("Shared/_LinkTransferForm", new LinkTransferViewModel { ErrorMessage = "Link not found." });

        var link = await FindLinkAsync(code.Value);
        if (link is null)
            return Partial("Shared/_LinkTransferForm", new LinkTransferViewModel { ErrorMessage = "Link not found." });

        var ownerUserId = await _identity.ResolveOwnerUserIdAsync(User);
        var workspaces = ownerUserId is not null
            ? await _workspaces.GetWorkspacesForUserAsync(ownerUserId.Value)
            : [];

        return Partial("Shared/_LinkTransferForm", new LinkTransferViewModel
        {
            Code = link.Id,
            CurrentWorkspace = link.Workspace?.Slug ?? "personal",
            Workspaces = workspaces
        });
    }

    public async Task<IActionResult> OnPostEdit(long code, string url, string slug, string title, string description, string tags)
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        Workspace = await _identity.ResolveActiveWorkspaceContextAsync(User);

        var link = await FindLinkAsync(code);
        if (link is null)
            return Partial("Shared/_LinkEditForm", new LinkEditViewModel { Code = code, ErrorMessage = "Link not found." });

        var trimmedUrl = url.Trim();
        if (!Uri.TryCreate(trimmedUrl, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
            return Partial("Shared/_LinkEditForm", LinkEditViewModel.From(link, "Enter a valid absolute http(s) URL."));

        var trimmedSlug = slug.Trim();
        if (trimmedSlug.Length == 0 || !ShortLinkCodes.IsValidSlug(trimmedSlug))
            return Partial("Shared/_LinkEditForm", LinkEditViewModel.From(link, "Slug must be 1–64 chars: letters, digits, '-' or '_', starting with a letter or digit."));

        var collides = await _db.ShortenedUrls.AnyAsync(l => l.Id != link.Id && l.DomainId == link.DomainId && l.ShortCode == trimmedSlug);
        if (collides)
            return Partial("Shared/_LinkEditForm", LinkEditViewModel.From(link, $"A link with slug '{trimmedSlug}' already exists on this domain."));

        link.LongUrl = trimmedUrl;
        link.ShortCode = trimmedSlug;
        link.Title = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        link.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        link.UpdatedAtUtc = DateTime.UtcNow;

        var tagNames = (tags ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.Length > 128 ? t[..128] : t)
            .Distinct()
            .ToList();
        var existingTags = await _db.ShortenedUrlTags.Where(t => t.ShortenedUrlId == link.Id).ToListAsync();
        _db.ShortenedUrlTags.RemoveRange(existingTags);
        _db.ShortenedUrlTags.AddRange(tagNames.Select(name => new ShortenedUrlTag
        {
            ShortenedUrlId = link.Id,
            Name = name,
            CreatedAtUtc = DateTime.UtcNow
        }));

        await _db.SaveChangesAsync();

        var links = await LoadLinksAsync(null, null, null, null, null);
        return Partial("Shared/_LinkEditSuccess", new LinkEditSuccessViewModel
        {
            Links = links,
            Message = $"Link updated. Click count preserved at {link.ClickCount}."
        });
    }

    public async Task<IActionResult> OnPostArchive(long code)
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        Workspace = await _identity.ResolveActiveWorkspaceContextAsync(User);

        var link = await FindLinkAsync(code);
        if (link is not null && link.ArchivedAtUtc is null)
        {
            link.ArchivedAtUtc = DateTime.UtcNow;
            link.UpdatedAtUtc = link.ArchivedAtUtc;
            await _db.SaveChangesAsync();
        }

        return Partial("Shared/_SearchResults", await LoadLinksAsync(null, null, null, null, null));
    }

    public async Task<IActionResult> OnPostUnarchive(long code)
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        Workspace = await _identity.ResolveActiveWorkspaceContextAsync(User);

        var link = await FindLinkAsync(code);
        if (link is not null && link.ArchivedAtUtc is not null)
        {
            link.ArchivedAtUtc = null;
            link.UpdatedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        return Partial("Shared/_SearchResults", await LoadLinksAsync(null, null, null, null, null));
    }

    public async Task<IActionResult> OnPostTransfer(long code, string workspace)
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        Workspace = await _identity.ResolveActiveWorkspaceContextAsync(User);

        var link = await FindLinkAsync(code);
        if (link is null)
            return Partial("Shared/_LinkTransferForm", new LinkTransferViewModel { Code = code, ErrorMessage = "Link not found." });

        var ownerUserId = await _identity.ResolveOwnerUserIdAsync(User);
        if (ownerUserId is null)
            return Partial("Shared/_LinkTransferForm", new LinkTransferViewModel { Code = code, ErrorMessage = "You must be signed in to transfer links." });

        var target = await _workspaces.GetWorkspaceBySlugAsync(workspace);
        if (target is null || !await _workspaces.IsMemberAsync(target.Id, ownerUserId.Value))
            return Partial("Shared/_LinkTransferForm", new LinkTransferViewModel
            {
                Code = code,
                CurrentWorkspace = link.Workspace?.Slug ?? "personal",
                Workspaces = await _workspaces.GetWorkspacesForUserAsync(ownerUserId.Value),
                ErrorMessage = "You can only transfer to a workspace you are a member of."
            });

        var sourceWorkspaceId = link.WorkspaceId;
        if (sourceWorkspaceId is not null && !await _workspaces.IsMemberAsync(sourceWorkspaceId.Value, ownerUserId.Value))
            return Partial("Shared/_LinkTransferForm", new LinkTransferViewModel
            {
                Code = code,
                CurrentWorkspace = link.Workspace?.Slug ?? "personal",
                Workspaces = await _workspaces.GetWorkspacesForUserAsync(ownerUserId.Value),
                ErrorMessage = "You must be a member of the link's current workspace to transfer it."
            });

        link.WorkspaceId = target.Id;
        link.OwnerUserId = null;
        link.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var links = await LoadLinksAsync(null, null, null, null, null);
        return Partial("Shared/_LinkTransferSuccess", new LinkTransferSuccessViewModel
        {
            Links = links,
            Message = $"Link moved to workspace '{target.Name}'. Click count preserved at {link.ClickCount}."
        });
    }

    private IActionResult? EnforceAccess()
    {
        if (_identity.IsAuthEnabled && User.Identity?.IsAuthenticated != true)
            return Request.Headers["HX-Request"].Count > 0
                ? Unauthorized()
                : RedirectToPage("/Index");

        return null;
    }

    private async Task<List<ShortenedUrl>> LoadLinksAsync(string? search, string? linkSort, string? linkDir, string? domain, string? status)
    {
        var ownerUserId = await _identity.ResolveOwnerUserIdAsync(User);
        var workspaceId = Workspace?.WorkspaceId;

        var linkQ = ApplyScoping(_db.ShortenedUrls.AsQueryable(), ownerUserId, workspaceId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var lower = search.ToLowerInvariant();
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

        linkQ = status switch
        {
            "archived" => linkQ.Where(l => l.ArchivedAtUtc != null),
            "all" => linkQ,
            _ => linkQ.Where(l => l.ArchivedAtUtc == null)
        };

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

        return await linkQ
            .AsNoTracking()
            .Include(l => l.Domain)
            .Include(l => l.Workspace)
            .Take(50)
            .ToListAsync();
    }

    private async Task<ShortenedUrl?> FindLinkAsync(long code)
    {
        var ownerUserId = await _identity.ResolveOwnerUserIdAsync(User);
        var workspaceId = Workspace?.WorkspaceId;

        var query = _db.ShortenedUrls
            .Include(l => l.Domain)
            .Include(l => l.Workspace)
            .AsQueryable();
        if (workspaceId is not null)
            query = query.Where(l => l.WorkspaceId == workspaceId);
        else if (ownerUserId is not null)
            query = query.Where(l => l.OwnerUserId == ownerUserId);

        return await query.FirstOrDefaultAsync(l => l.Id == code);
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