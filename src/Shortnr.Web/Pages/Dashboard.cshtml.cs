using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Web.Features.ClickTracking;
using Shortnr.Web.Features.Infrastructure;
using Shortnr.Web.Features.ShortLinks;
using Shortnr.Web.Features.Workspaces;

namespace Shortnr.Web.Pages;

public class DashboardModel : PageModel, IStatusMessages
{
    private readonly AppDbContext _db;
    private readonly UserIdentityService _identity;
    private readonly WorkspaceService _workspaces;
    private readonly BulkLinkUndoService _undo;

    public DashboardModel(AppDbContext db, UserIdentityService identity, WorkspaceService workspaces, BulkLinkUndoService undo)
    {
        _db = db;
        _identity = identity;
        _workspaces = workspaces;
        _undo = undo;
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

            var clicksLast7Days = await clickQuery
                .Where(e => e.ClickedAtUtc >= DateTime.UtcNow.AddDays(-7))
                .LongCountAsync();

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
                ClicksLast7Days = clicksLast7Days,
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

        return Partial("Shared/_LinkEditForm", LinkEditViewModel.From(link, pixelSnippets: await LoadPixelSnippetsAsync()));
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
            return Partial("Shared/_LinkEditForm", LinkEditViewModel.From(link, "Enter a valid absolute http(s) URL.", await LoadPixelSnippetsAsync()));

        var trimmedSlug = slug.Trim();
        if (trimmedSlug.Length == 0 || !ShortLinkCodes.IsValidSlug(trimmedSlug))
            return Partial("Shared/_LinkEditForm", LinkEditViewModel.From(link, "Slug must be 1–64 chars: letters, digits, '-' or '_', starting with a letter or digit.", await LoadPixelSnippetsAsync()));

        var collides = await _db.ShortenedUrls.AnyAsync(l => l.Id != link.Id && l.DomainId == link.DomainId && l.ShortCode == trimmedSlug);
        if (collides)
            return Partial("Shared/_LinkEditForm", LinkEditViewModel.From(link, $"A link with slug '{trimmedSlug}' already exists on this domain.", await LoadPixelSnippetsAsync()));

        var utm = new UtmParameters(
            Source: Request.Form["utm_source"].FirstOrDefault(),
            Medium: Request.Form["utm_medium"].FirstOrDefault(),
            Campaign: Request.Form["utm_campaign"].FirstOrDefault(),
            Term: Request.Form["utm_term"].FirstOrDefault(),
            Content: Request.Form["utm_content"].FirstOrDefault());
        if (!utm.IsEmpty)
            trimmedUrl = UtmBuilder.AppendUtm(trimmedUrl, utm);

        var pixelSnippetId = long.TryParse(Request.Form["pixel_type"].FirstOrDefault(), out var parsedPixelId) ? parsedPixelId : (long?)null;
        var pixelValue = await ResolvePixelValueAsync(pixelSnippetId,
            Request.Form["pixel_id"].FirstOrDefault(),
            Request.Form["pixel_snippet"].FirstOrDefault());
        var trimmedIosDeepLink = (Request.Form["ios_deep_link"].FirstOrDefault() ?? "").Trim();
        var trimmedAndroidDeepLink = (Request.Form["android_deep_link"].FirstOrDefault() ?? "").Trim();

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

        var hasMetadata = !utm.IsEmpty || pixelSnippetId is not null
            || trimmedIosDeepLink.Length > 0 || trimmedAndroidDeepLink.Length > 0;
        if (hasMetadata)
        {
            if (link.Metadata is null)
            {
                link.Metadata = new ShortenedUrlMetadata { ShortenedUrlId = link.Id };
                _db.ShortenedUrlMetadatas.Add(link.Metadata);
            }
            link.Metadata.UtmSource = utm.Source;
            link.Metadata.UtmMedium = utm.Medium;
            link.Metadata.UtmCampaign = utm.Campaign;
            link.Metadata.UtmTerm = utm.Term;
            link.Metadata.UtmContent = utm.Content;
            link.Metadata.PixelSnippetId = pixelSnippetId;
            link.Metadata.PixelId = pixelValue;
            link.Metadata.IosDeepLink = trimmedIosDeepLink.Length > 0 ? trimmedIosDeepLink : null;
            link.Metadata.AndroidDeepLink = trimmedAndroidDeepLink.Length > 0 ? trimmedAndroidDeepLink : null;
        }
        else if (link.Metadata is not null)
        {
            _db.ShortenedUrlMetadatas.Remove(link.Metadata);
            link.Metadata = null;
        }

        await _db.SaveChangesAsync();

        var links = await LoadLinksAsync(null, null, null, null, null);
        return Partial("Shared/_LinkEditSuccess", new LinkEditSuccessViewModel
        {
            Links = links,
            Message = $"Link updated. Click count preserved at {link.ClickCount}."
        });
    }

    public async Task<IActionResult> OnPostArchive(long code, string? search, string? linkSort, string? linkDir, string? domain, string? status)
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

        return Partial("Shared/_SearchResults", await LoadLinksAsync(search, linkSort, linkDir, domain, status));
    }

    public async Task<IActionResult> OnPostUnarchive(long code, string? search, string? linkSort, string? linkDir, string? domain, string? status)
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

        return Partial("Shared/_SearchResults", await LoadLinksAsync(search, linkSort, linkDir, domain, status));
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

    // -------------------------------------------------------------------------
    // PRD-019: link-detail drill-down, bulk actions, and undo
    // -------------------------------------------------------------------------

    public async Task<IActionResult> OnGetDetail(long? code)
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        Workspace = await _identity.ResolveActiveWorkspaceContextAsync(User);

        if (code is null)
            return NotFound();

        var link = await FindLinkAsync(code.Value);
        if (link is null)
            return NotFound();

        return Partial("Shared/_LinkDetail", await BuildDetailAsync(link));
    }

    public async Task<IActionResult> OnPostBulkDelete([FromForm] long[] ids, string? search, string? linkSort, string? linkDir, string? domain, string? status)
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        Workspace = await _identity.ResolveActiveWorkspaceContextAsync(User);
        var ownerUserId = await _identity.ResolveOwnerUserIdAsync(User);
        var workspaceId = Workspace?.WorkspaceId;

        var idsSet = ids.Distinct().ToHashSet();
        if (idsSet.Count == 0)
            return Partial("Shared/_BulkActionResult", new BulkActionResultViewModel
            {
                Links = await LoadLinksAsync(search, linkSort, linkDir, domain, status),
                Message = "No links selected.",
                Kind = StatusKind.Neutral
            });

        // Snapshot for undo before deleting, scoped so only the caller's own
        // links (personal or current workspace) can ever be captured.
        var scoped = await FindLinksByIdsAsync(idsSet);
        if (scoped.Count == 0)
            return Partial("Shared/_BulkActionResult", new BulkActionResultViewModel
            {
                Links = await LoadLinksAsync(search, linkSort, linkDir, domain, status),
                Message = "No links selected.",
                Kind = StatusKind.Neutral
            });

        var captured = scoped.ToList();
        var tokens = _undo.Capture(captured);

        _db.ShortenedUrls.RemoveRange(scoped);
        await _db.SaveChangesAsync();

        var links = await LoadLinksAsync(search, linkSort, linkDir, domain, status);
        return Partial("Shared/_BulkActionResult", new BulkActionResultViewModel
        {
            Links = links,
            Message = $"Deleted {captured.Count} link{(captured.Count == 1 ? "" : "s")}.",
            Kind = StatusKind.Success,
            UndoToken = tokens
        });
    }

    public async Task<IActionResult> OnPostBulkArchive([FromForm] long[] ids, string? search, string? linkSort, string? linkDir, string? domain, string? status)
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        Workspace = await _identity.ResolveActiveWorkspaceContextAsync(User);
        var ownerUserId = await _identity.ResolveOwnerUserIdAsync(User);
        var workspaceId = Workspace?.WorkspaceId;

        var idsSet = ids.Distinct().ToHashSet();
        var now = DateTime.UtcNow;
        var count = await ApplyScoping(_db.ShortenedUrls.AsQueryable(), ownerUserId, workspaceId)
            .Where(l => idsSet.Contains(l.Id) && l.ArchivedAtUtc == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(l => l.ArchivedAtUtc, now)
                .SetProperty(l => l.UpdatedAtUtc, now));

        var links = await LoadLinksAsync(search, linkSort, linkDir, domain, status);
        return Partial("Shared/_BulkActionResult", new BulkActionResultViewModel
        {
            Links = links,
            Message = count == 0 ? "No active links selected." : $"Archived {count} link{(count == 1 ? "" : "s")}.",
            Kind = StatusKind.Info
        });
    }

    public async Task<IActionResult> OnPostBulkUnarchive([FromForm] long[] ids, string? search, string? linkSort, string? linkDir, string? domain, string? status)
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        Workspace = await _identity.ResolveActiveWorkspaceContextAsync(User);
        var ownerUserId = await _identity.ResolveOwnerUserIdAsync(User);
        var workspaceId = Workspace?.WorkspaceId;

        var idsSet = ids.Distinct().ToHashSet();
        var count = await ApplyScoping(_db.ShortenedUrls.AsQueryable(), ownerUserId, workspaceId)
            .Where(l => idsSet.Contains(l.Id) && l.ArchivedAtUtc != null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(l => l.ArchivedAtUtc, (DateTime?)null)
                .SetProperty(l => l.UpdatedAtUtc, DateTime.UtcNow));

        var links = await LoadLinksAsync(search, linkSort, linkDir, domain, status);
        return Partial("Shared/_BulkActionResult", new BulkActionResultViewModel
        {
            Links = links,
            Message = count == 0 ? "No archived links selected." : $"Restored {count} link{(count == 1 ? "" : "s")}.",
            Kind = StatusKind.Info
        });
    }

    public async Task<IActionResult> OnPostBulkMove([FromForm] long[] ids, string workspace, string? search, string? linkSort, string? linkDir, string? domain, string? status)
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        Workspace = await _identity.ResolveActiveWorkspaceContextAsync(User);
        var ownerUserId = await _identity.ResolveOwnerUserIdAsync(User);
        var workspaceId = Workspace?.WorkspaceId;

        var idsSet = ids.Distinct().ToHashSet();
        if (idsSet.Count == 0)
            return Partial("Shared/_BulkActionResult", new BulkActionResultViewModel
            {
                Links = await LoadLinksAsync(search, linkSort, linkDir, domain, status),
                Message = "No links selected.",
                Kind = StatusKind.Neutral
            });

        var movesForUser = ownerUserId ?? await _identity.ResolveOwnerUserIdAsync(User);
        if (movesForUser is null)
            return Partial("Shared/_BulkActionResult", new BulkActionResultViewModel
            {
                Links = await LoadLinksAsync(search, linkSort, linkDir, domain, status),
                Message = "You must be signed in to move links.",
                Kind = StatusKind.Error
            });

        var target = await _workspaces.GetWorkspaceBySlugAsync(workspace);
        if (target is null || !await _workspaces.IsMemberAsync(target.Id, movesForUser.Value))
            return Partial("Shared/_BulkActionResult", new BulkActionResultViewModel
            {
                Links = await LoadLinksAsync(search, linkSort, linkDir, domain, status),
                Message = "You can only move links to a workspace you are a member of.",
                Kind = StatusKind.Error
            });

        var count = await ApplyScoping(_db.ShortenedUrls.AsQueryable(), ownerUserId, workspaceId)
            .Where(l => idsSet.Contains(l.Id))
            .ExecuteUpdateAsync(s => s
                .SetProperty(l => l.WorkspaceId, target.Id)
                .SetProperty(l => l.OwnerUserId, (long?)null)
                .SetProperty(l => l.UpdatedAtUtc, DateTime.UtcNow));

        var links = await LoadLinksAsync(search, linkSort, linkDir, domain, status);
        return Partial("Shared/_BulkActionResult", new BulkActionResultViewModel
        {
            Links = links,
            Message = $"Moved {count} link{(count == 1 ? "" : "s")} to '{target.Name}'.",
            Kind = StatusKind.Info
        });
    }

    public async Task<IActionResult> OnPostBulkTag([FromForm] long[] ids, string tags, string? search, string? linkSort, string? linkDir, string? domain, string? status)
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        Workspace = await _identity.ResolveActiveWorkspaceContextAsync(User);
        var ownerUserId = await _identity.ResolveOwnerUserIdAsync(User);
        var workspaceId = Workspace?.WorkspaceId;

        var idsSet = ids.Distinct().ToHashSet();
        var tagNames = (tags ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.Length > 128 ? t[..128] : t)
            .Distinct()
            .ToList();

        if (idsSet.Count == 0 || tagNames.Count == 0)
            return Partial("Shared/_BulkActionResult", new BulkActionResultViewModel
            {
                Links = await LoadLinksAsync(search, linkSort, linkDir, domain, status),
                Message = tagNames.Count == 0 ? "Enter at least one tag." : "No links selected.",
                Kind = StatusKind.Neutral
            });

        var links = await ApplyScoping(_db.ShortenedUrls.AsQueryable(), ownerUserId, workspaceId)
            .Where(l => idsSet.Contains(l.Id))
            .Include(l => l.Tags)
            .ToListAsync();

        var now = DateTime.UtcNow;
        foreach (var link in links)
        {
            var existing = link.Tags.Select(t => t.Name).ToHashSet();
            var toAdd = tagNames.Where(n => !existing.Contains(n)).ToList();
            if (toAdd.Count == 0)
                continue;
            foreach (var name in toAdd)
            {
                _db.ShortenedUrlTags.Add(new ShortenedUrlTag
                {
                    ShortenedUrlId = link.Id,
                    Name = name,
                    CreatedAtUtc = now
                });
            }
            link.UpdatedAtUtc = now;
        }
        await _db.SaveChangesAsync();

        var resultLinks = await LoadLinksAsync(search, linkSort, linkDir, domain, status);
        return Partial("Shared/_BulkActionResult", new BulkActionResultViewModel
        {
            Links = resultLinks,
            Message = $"Tagged {links.Count} link{(links.Count == 1 ? "" : "s")} with {string.Join(", ", tagNames)}.",
            Kind = StatusKind.Info
        });
    }

    public async Task<IActionResult> OnPostBulkUntag([FromForm] long[] ids, string tags, string? search, string? linkSort, string? linkDir, string? domain, string? status)
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        Workspace = await _identity.ResolveActiveWorkspaceContextAsync(User);
        var ownerUserId = await _identity.ResolveOwnerUserIdAsync(User);
        var workspaceId = Workspace?.WorkspaceId;

        var idsSet = ids.Distinct().ToHashSet();
        var tagNames = (tags ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct()
            .ToList();

        if (idsSet.Count == 0 || tagNames.Count == 0)
            return Partial("Shared/_BulkActionResult", new BulkActionResultViewModel
            {
                Links = await LoadLinksAsync(search, linkSort, linkDir, domain, status),
                Message = "Enter at least one tag.",
                Kind = StatusKind.Neutral
            });

        var links = await ApplyScoping(_db.ShortenedUrls.AsQueryable(), ownerUserId, workspaceId)
            .Where(l => idsSet.Contains(l.Id))
            .Include(l => l.Tags)
            .ToListAsync();

        var removed = 0;
        foreach (var link in links)
        {
            var matching = link.Tags.Where(t => tagNames.Any(n => string.Equals(n, t.Name, StringComparison.OrdinalIgnoreCase))).ToList();
            if (matching.Count == 0)
                continue;
            foreach (var tag in matching)
                _db.ShortenedUrlTags.Remove(tag);
            removed++;
            link.UpdatedAtUtc = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync();

        var resultLinks = await LoadLinksAsync(search, linkSort, linkDir, domain, status);
        return Partial("Shared/_BulkActionResult", new BulkActionResultViewModel
        {
            Links = resultLinks,
            Message = removed == 0 ? "No matching tags found on the selected links." : $"Removed tag(s) from {removed} link{(removed == 1 ? "" : "s")}.",
            Kind = StatusKind.Info
        });
    }

    public async Task<IActionResult> OnPostUndo(string token, string? search, string? linkSort, string? linkDir, string? domain, string? status)
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        Workspace = await _identity.ResolveActiveWorkspaceContextAsync(User);

        var snapshots = _undo.Retrieve(token);
        if (snapshots is null || snapshots.Count == 0)
            return Partial("Shared/_BulkActionResult", new BulkActionResultViewModel
            {
                Links = await LoadLinksAsync(search, linkSort, linkDir, domain, status),
                Message = "Undo is no longer available for those links.",
                Kind = StatusKind.Error
            });

        // Re-insert with stable primary keys so referential data (clicks, tags,
        // metadata) that was never touched is preserved.
        foreach (var link in snapshots)
        {
            _db.ShortenedUrls.Add(link);
            if (link.Metadata is not null)
                _db.ShortenedUrlMetadatas.Add(link.Metadata);
            foreach (var tag in link.Tags)
                _db.ShortenedUrlTags.Add(tag);
        }
        await _db.SaveChangesAsync();

        var links = await LoadLinksAsync(search, linkSort, linkDir, domain, status);
        return Partial("Shared/_BulkActionResult", new BulkActionResultViewModel
        {
            Links = links,
            Message = $"Restored {snapshots.Count} link{(snapshots.Count == 1 ? "" : "s")}.",
            Kind = StatusKind.Success
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

    private async Task<LinkDetailViewModel> BuildDetailAsync(ShortenedUrl link)
    {
        var ownerUserId = await _identity.ResolveOwnerUserIdAsync(User);
        var workspaceId = Workspace?.WorkspaceId;

        var clickQuery = ApplyClickScoping(_db.ClickEvents.AsQueryable(), ownerUserId, workspaceId)
            .Where(e => e.ShortenedUrlId == link.Id);

        // Click timeline — last 30 days, padded so the chart always spans the
        // full window even when a link is brand new.
        var since = DateTime.UtcNow.Date.AddDays(-29);
        var rawDays = await clickQuery
            .Where(e => e.ClickedAtUtc >= since)
            .GroupBy(e => e.ClickedAtUtc.Date)
            .Select(g => new { Day = g.Key, Count = g.LongCount() })
            .ToListAsync();
        var timeline = new List<TimelinePoint>(30);
        for (var i = 0; i < 30; i++)
        {
            var day = since.AddDays(i);
            timeline.Add(new TimelinePoint
            {
                Label = day.ToString("MMM d"),
                Count = rawDays.FirstOrDefault(d => d.Day == day)?.Count ?? 0
            });
        }

        // Referrers — top raw referers roll up by normalized host so "twitter.com/x"
        // and "twitter.com/y" count as one domain. Bounded in SQL (top 20) before
        // the in-memory host extraction.
        var rawReferrers = await clickQuery
            .Where(e => e.Referer.Length > 0 && e.Referer != "direct")
            .GroupBy(e => e.Referer)
            .Select(g => new { Ref = g.Key, Count = g.LongCount() })
            .OrderByDescending(x => x.Count)
            .Take(20)
            .ToListAsync();
        var referrers = rawReferrers
            .Select(r => new { Domain = RefererHost(r.Ref), Count = r.Count })
            .GroupBy(x => x.Domain)
            .Select(g => new NameCountStat { Name = g.Key, Count = g.Sum(x => x.Count) })
            .OrderByDescending(x => x.Count)
            .Take(10)
            .ToList();
        if (referrers.Count == 0 && await clickQuery.CountAsync() > 0)
            referrers.Add(new NameCountStat { Name = "Direct / unknown", Count = await clickQuery.LongCountAsync() });

        // Devices — roll UAParser's DeviceFamily/counts into the three-class
        // desktop/mobile/tablet split the PRD calls for, with an "Unknown" bucket
        // for clicks whose device family was never persisted.
        var rawDevices = await clickQuery
            .Where(e => e.DeviceFamily != null && e.DeviceFamily != "")
            .GroupBy(e => e.DeviceFamily!)
            .Select(g => new { Device = g.Key, Count = g.LongCount() })
            .ToListAsync();
        var noneDevice = await clickQuery
            .Where(e => e.DeviceFamily == null || e.DeviceFamily == "")
            .LongCountAsync();
        var devices = rawDevices
            .Select(d => new NameCountStat { Name = DeviceClass(d.Device), Count = d.Count })
            .GroupBy(x => x.Name)
            .Select(g => new NameCountStat { Name = g.Key, Count = g.Sum(x => x.Count) })
            .OrderByDescending(x => x.Count)
            .ToList();
        if (noneDevice > 0)
            devices = devices
                .Append(new NameCountStat { Name = "Unknown", Count = noneDevice })
                .OrderByDescending(x => x.Count)
                .ToList();

        // Geo — country split (top 10) plus the top cities under it.
        var geoRows = await clickQuery
            .Where(e => e.CountryCode != null && e.CountryCode != "")
            .GroupBy(e => new { e.CountryCode, e.CountryName })
            .Select(g => new { g.Key.CountryCode, g.Key.CountryName, Count = g.LongCount() })
            .OrderByDescending(x => x.Count)
            .Take(10)
            .ToListAsync();
        var geo = geoRows
            .Select(g => new NameCountStat { Name = $"{g.CountryCode} — {g.CountryName}", Count = g.Count })
            .ToList();
        var cityRows = await clickQuery
            .Where(e => e.CityName != null && e.CityName != "")
            .GroupBy(e => e.CityName!)
            .Select(g => new { City = g.Key, Count = g.LongCount() })
            .OrderByDescending(x => x.Count)
            .Take(5)
            .ToListAsync();
        var cities = cityRows
            .Select(c => new NameCountStat { Name = c.City, Count = c.Count })
            .ToList();

        // UTM summary — the params this link was created with, only those set.
        var utm = link.Metadata is null
            ? []
            : new List<NameCountStat>()
            {
                FromUtm("source", link.Metadata.UtmSource),
                FromUtm("medium", link.Metadata.UtmMedium),
                FromUtm("campaign", link.Metadata.UtmCampaign),
                FromUtm("term", link.Metadata.UtmTerm),
                FromUtm("content", link.Metadata.UtmContent)
            }.Where(s => s is not null).Cast<NameCountStat>().ToList();

        var chartJson = JsonSerializer.Serialize(new
        {
            timeline = timeline.Select(t => new { label = t.Label, count = t.Count }),
            devices = devices.Select(d => new { name = d.Name, count = d.Count })
        });

        var scheme = Request.Scheme;
        return new LinkDetailViewModel
        {
            Id = link.Id,
            ShortCode = link.ShortCode,
            LongUrl = link.LongUrl,
            Title = link.Title,
            Description = link.Description,
            CreatedAtUtc = link.CreatedAtUtc,
            ClickCount = link.ClickCount,
            DomainHostname = link.Domain?.Hostname ?? "",
            IsArchived = link.IsArchived,
            Tags = link.Tags?.Select(t => t.Name).ToList() ?? [],
            Timeline = timeline,
            Referrers = referrers,
            Devices = devices,
            Geo = geo,
            Cities = cities,
            Utm = utm,
            ChartJson = chartJson,
            DisplayHref = ShortUrlHelper.DisplayHref(scheme, link.Domain?.Hostname, link.ShortCode)
        };
    }

    private static string RefererHost(string referer)
    {
        if (string.IsNullOrWhiteSpace(referer)) return "Direct / unknown";
        if (Uri.TryCreate(referer, UriKind.Absolute, out var uri)) return uri.Host;
        return referer.Length > 40 ? referer[..40] : referer;
    }

    private static string DeviceClass(string deviceFamily) => deviceFamily.ToLowerInvariant() switch
    {
        "mobile" or "smartphone" or "phone" or "ios" or "android" => "Mobile",
        "tablet" or "ipad" => "Tablet",
        "unknown" or "" => "Unknown",
        _ => "Desktop"
    };

    private static NameCountStat? FromUtm(string param, string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : new NameCountStat { Name = $"{param}: {value}", Count = 1 };

    private async Task<List<ShortenedUrl>> FindLinksByIdsAsync(IEnumerable<long> ids)
    {
        var ownerUserId = await _identity.ResolveOwnerUserIdAsync(User);
        var workspaceId = Workspace?.WorkspaceId;
        var set = ids.ToHashSet();
        return await ApplyScoping(_db.ShortenedUrls.AsQueryable(), ownerUserId, workspaceId)
            .Where(l => set.Contains(l.Id))
            .Include(l => l.Tags)
            .Include(l => l.Metadata)
            .ToListAsync();
    }

    private async Task<List<LinkRowViewModel>> LoadLinksAsync(string? search, string? linkSort, string? linkDir, string? domain, string? status)
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
            .Select(l => new LinkRowViewModel
            {
                Id = l.Id,
                ShortCode = l.ShortCode,
                LongUrl = l.LongUrl,
                CreatedAtUtc = l.CreatedAtUtc,
                ClickCount = l.ClickCount,
                LastClickedAtUtc = l.ClickEvents
                    .OrderByDescending(e => e.ClickedAtUtc)
                    .Select(e => (DateTime?)e.ClickedAtUtc)
                    .FirstOrDefault(),
                DomainHostname = l.Domain == null ? "" : l.Domain.Hostname,
                IsArchived = l.ArchivedAtUtc != null,
                Tags = l.Tags.Select(t => t.Name).ToList()
            })
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
            .Include(l => l.Tags)
            .Include(l => l.Metadata)
            .ThenInclude(m => m!.PixelSnippet)
            .AsQueryable();
        if (workspaceId is not null)
            query = query.Where(l => l.WorkspaceId == workspaceId);
        else if (ownerUserId is not null)
            query = query.Where(l => l.OwnerUserId == ownerUserId);

        return await query.FirstOrDefaultAsync(l => l.Id == code);
    }

    private Task<List<PixelSnippet>> LoadPixelSnippetsAsync() =>
        _db.PixelSnippets.OrderBy(p => p.Id).ToListAsync();

    /// <summary>
    /// Resolves the metadata's PixelId value: the pixel ID for template snippets,
    /// or the full pasted HTML for the custom snippet. Mirrors Index.cshtml.cs's
    /// identically-named helper for the create form.
    /// </summary>
    private async Task<string?> ResolvePixelValueAsync(long? pixelSnippetId, string? pixelId, string? customSnippet)
    {
        if (pixelSnippetId is null)
            return null;

        var snippet = await _db.PixelSnippets.FirstOrDefaultAsync(p => p.Id == pixelSnippetId);
        if (snippet is null)
            return null;

        return snippet.IsCustom ? customSnippet : pixelId;
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