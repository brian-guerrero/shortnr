using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Shortnr.Web.Features.Mcp.McpTools;

/// <summary>
/// Read-only short-link tools: listing, stats aggregation, and "most popular"
/// queries. Every tool requires the <c>mcp:read</c> scope on the calling API key.
/// </summary>
[McpServerToolType]
public static class McpLinkReadTools
{
    [McpServerTool(Name = "list_links", Title = "List short links", ReadOnly = true)]
    public static async Task<string> ListLinks(
        RequestContext<CallToolRequestParams> context,
        AppDbContext db,
        UserIdentityService identity,
        [Description("Optional case-insensitive filter on short code or destination URL")] string? filter = null,
        [Description("Optional case-insensitive filter on UTM campaign — find every link created for a given campaign")] string? campaign = null,
        [Description("Sort order: 'created' (newest first, default), 'clicks_desc', 'clicks_asc'")] string? sort = null,
        [Description("Only links on this verified domain hostname, or 'default' for the instance host")] string? domain = null,
        [Description("Lifecycle status: 'all' (default), 'active', 'archived'")] string? status = null,
        [Description("Maximum number of links to return (1-100)")] int limit = 50,
        CancellationToken ct = default)
    {
        var ownerUserId = await McpToolGuard.ResolveOwnerAsync(context, identity);
        if (ownerUserId is null) return McpToolGuard.OwnerError;
        if (!McpToolGuard.HasScope(context, ApiKeyScopes.McpRead)) return McpToolGuard.ReadScopeError;

        limit = Math.Clamp(limit, 1, 100);

        var query = db.ShortenedUrls.Where(l => l.OwnerUserId == ownerUserId);
        if (!string.IsNullOrWhiteSpace(domain))
        {
            query = domain.Trim().ToLowerInvariant() == "default"
                ? query.Where(l => l.DomainId == null)
                : query.Where(l => l.Domain != null && l.Domain.Hostname == domain.Trim().ToLowerInvariant());
        }
        if (!string.IsNullOrWhiteSpace(filter))
        {
            var f = filter.Trim();
            query = query.Where(l => l.ShortCode.Contains(f) || l.LongUrl.Contains(f));
        }
        if (!string.IsNullOrWhiteSpace(campaign))
        {
            var c = campaign.Trim();
            query = query.Where(l => l.Metadata != null && l.Metadata.UtmCampaign != null && l.Metadata.UtmCampaign.Contains(c));
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = status.Trim().ToLowerInvariant() switch
            {
                "active" => query.Where(l => l.ArchivedAtUtc == null),
                "archived" => query.Where(l => l.ArchivedAtUtc != null),
                "all" => query,
                _ => query
            };
        }

        var ordered = sort switch
        {
            "clicks_desc" => query.OrderByDescending(l => l.ClickCount),
            "clicks_asc" => query.OrderBy(l => l.ClickCount),
            null or "" or "created" => query.OrderByDescending(l => l.CreatedAtUtc),
            _ => null
        };
        if (ordered is null)
            return $"Error: invalid sort '{sort}'. Expected 'created', 'clicks_desc' or 'clicks_asc'.";

        var links = await ordered
            .Take(limit)
            .Select(l => new LinkListItem(
                l.ShortCode,
                l.Domain != null ? l.Domain.Hostname : null,
                l.LongUrl,
                l.ClickCount,
                l.CreatedAtUtc,
                l.Title,
                l.Description,
                l.Tags.OrderBy(t => t.Name).Select(t => t.Name).ToList(),
                l.ArchivedAtUtc,
                l.Metadata == null ? null : new LinkMetadataResult(
                    l.Metadata.UtmSource, l.Metadata.UtmMedium, l.Metadata.UtmCampaign, l.Metadata.UtmTerm, l.Metadata.UtmContent,
                    l.Metadata.PixelSnippet == null ? null : l.Metadata.PixelSnippet.Name,
                    l.Metadata.PixelId, l.Metadata.IosDeepLink, l.Metadata.AndroidDeepLink)))
            .ToListAsync(ct);

        return links.Count == 0
            ? "No links found."
            : McpToolGuard.Json(links);
    }

    [McpServerTool(Name = "get_link_stats", Title = "Get link stats", ReadOnly = true)]
    public static async Task<string> GetLinkStats(
        RequestContext<CallToolRequestParams> context,
        AppDbContext db,
        UserIdentityService identity,
        [Description("The short code of the link")] string short_code,
        CancellationToken ct = default)
    {
        var ownerUserId = await McpToolGuard.ResolveOwnerAsync(context, identity);
        if (ownerUserId is null) return McpToolGuard.OwnerError;
        if (!McpToolGuard.HasScope(context, ApiKeyScopes.McpRead)) return McpToolGuard.ReadScopeError;

        var link = await McpToolGuard.ResolveOwnedLinkAsync(db, ownerUserId.Value, short_code.Trim(), ct);
        if (link is null)
            return $"Error: no link with short code '{short_code}' found.";

        var clickQuery = db.ClickEvents.Where(e => e.ShortenedUrlId == link.Id);
        var total = await clickQuery.CountAsync(ct);

        var topReferrers = await GroupCounts(clickQuery
            .Where(e => e.Referer != "")
            .GroupBy(e => e.Referer), ct);
        var topCountries = await GroupCounts(clickQuery
            .Where(e => e.CountryName != null)
            .GroupBy(e => e.CountryName!), ct);
        var topDevices = await GroupCounts(clickQuery
            .Where(e => e.DeviceFamily != null && e.DeviceFamily != "")
            .GroupBy(e => e.DeviceFamily!), ct);
        var topBrowsers = await GroupCounts(clickQuery
            .Where(e => e.Browser != null && e.Browser != "")
            .GroupBy(e => e.Browser!), ct);

        var metadata = link.Metadata is null
            ? null
            : new LinkMetadataResult(
                link.Metadata.UtmSource, link.Metadata.UtmMedium, link.Metadata.UtmCampaign,
                link.Metadata.UtmTerm, link.Metadata.UtmContent,
                link.Metadata.PixelSnippet?.Name, link.Metadata.PixelId,
                link.Metadata.IosDeepLink, link.Metadata.AndroidDeepLink);

        return McpToolGuard.Json(new LinkStats(
            link.ShortCode,
            link.Domain?.Hostname,
            link.LongUrl,
            total,
            link.Title,
            link.Description,
            (await db.ShortenedUrlTags
                .Where(t => t.ShortenedUrlId == link.Id)
                .OrderBy(t => t.Name)
                .Select(t => t.Name)
                .ToListAsync(ct)),
            link.ArchivedAtUtc,
            metadata,
            topReferrers,
            topCountries,
            topDevices,
            topBrowsers));
    }

    private static async Task<List<NameCount>> GroupCounts(IQueryable<IGrouping<string, ClickEvent>> grouped, CancellationToken ct)
    {
        // SQLite's provider cannot translate a constructor projection carrying a
        // GroupBy aggregate, so project to an anonymous type and map afterwards.
        var rows = await grouped
            .Select(g => new { Name = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(5)
            .ToListAsync(ct);
        return rows.Select(x => new NameCount(x.Name, x.Count)).ToList();
    }

    [McpServerTool(Name = "get_top_links", Title = "Get top links by clicks", ReadOnly = true)]
    public static async Task<string> GetTopLinks(
        RequestContext<CallToolRequestParams> context,
        AppDbContext db,
        UserIdentityService identity,
        [Description("Period: 'all' (default), '7d', '30d', '90d'")] string? period = "all",
        [Description("Number of links to return (1-25)")] int limit = 5,
        CancellationToken ct = default)
    {
        var ownerUserId = await McpToolGuard.ResolveOwnerAsync(context, identity);
        if (ownerUserId is null) return McpToolGuard.OwnerError;
        if (!McpToolGuard.HasScope(context, ApiKeyScopes.McpRead)) return McpToolGuard.ReadScopeError;

        limit = Math.Clamp(limit, 1, 25);

        var now = DateTime.UtcNow;
        var periodValue = period?.Trim().ToLowerInvariant();
        var validPeriod = periodValue is null or "" or "all" or "7d" or "30d" or "90d";
        if (!validPeriod)
            return $"Error: invalid period '{period}'. Expected 'all', '7d', '30d' or '90d'.";

        var cutoff = periodValue switch
        {
            "7d" => now.AddDays(-7),
            "30d" => now.AddDays(-30),
            "90d" => now.AddDays(-90),
            _ => (DateTime?)null
        };

        var clickQuery = db.ClickEvents.Where(e => e.ShortenedUrl.OwnerUserId == ownerUserId);
        if (cutoff is not null)
            clickQuery = clickQuery.Where(e => e.ClickedAtUtc >= cutoff);

        var top = (await clickQuery
            .GroupBy(e => e.ShortenedUrlId)
            .Select(g => new { ShortenedUrlId = g.Key, Clicks = g.Count() })
            .OrderByDescending(x => x.Clicks)
            .Take(limit)
            .ToListAsync(ct))
            .Select(x => new TopLink(x.ShortenedUrlId, x.Clicks))
            .ToList();

        if (top.Count == 0)
            return "No clicks in this period.";

        var ids = top.Select(t => t.ShortenedUrlId).ToList();
        var links = await db.ShortenedUrls
            .AsNoTracking()
            .Where(l => ids.Contains(l.Id))
            .Include(l => l.Domain)
            .Include(l => l.Metadata)
            .ThenInclude(m => m!.PixelSnippet)
            .ToListAsync(ct);

        var result = top
            .Join(links, t => t.ShortenedUrlId, l => l.Id, (t, l) => new LinkListItem(
                l.ShortCode,
                l.Domain?.Hostname,
                l.LongUrl,
                t.Clicks,
                l.CreatedAtUtc,
                Metadata: l.Metadata is null ? null : new LinkMetadataResult(
                    l.Metadata.UtmSource, l.Metadata.UtmMedium, l.Metadata.UtmCampaign,
                    l.Metadata.UtmTerm, l.Metadata.UtmContent,
                    l.Metadata.PixelSnippet?.Name, l.Metadata.PixelId,
                    l.Metadata.IosDeepLink, l.Metadata.AndroidDeepLink)))
            .ToList();

        return McpToolGuard.Json(result);
    }

    [McpServerTool(Name = "list_pixel_snippets", Title = "List available retargeting pixel snippets", ReadOnly = true)]
    public static async Task<string> ListPixelSnippets(
        RequestContext<CallToolRequestParams> context,
        AppDbContext db,
        UserIdentityService identity,
        CancellationToken ct = default)
    {
        var ownerUserId = await McpToolGuard.ResolveOwnerAsync(context, identity);
        if (ownerUserId is null) return McpToolGuard.OwnerError;
        if (!McpToolGuard.HasScope(context, ApiKeyScopes.McpRead)) return McpToolGuard.ReadScopeError;

        var snippets = await db.PixelSnippets
            .OrderBy(p => p.Id)
            .Select(p => new PixelSnippetItem(p.Name, p.IsCustom))
            .ToListAsync(ct);

        return McpToolGuard.Json(snippets);
    }

    private sealed record LinkListItem(
        string ShortCode, string? Domain, string LongUrl, long ClickCount, DateTime CreatedAtUtc,
        string? Title = null, string? Description = null, IReadOnlyList<string>? Tags = null, DateTime? ArchivedAtUtc = null,
        LinkMetadataResult? Metadata = null);
    private sealed record NameCount(string Name, int Count);
    private sealed record TopLink(long ShortenedUrlId, int Clicks);
    /// <summary>Campaign metadata surfaced on link results: UTM components, the
    /// resolved retargeting pixel snippet's name (not its numeric id) and value,
    /// and platform deep links. Pass <c>PixelSnippet</c> back as create_short_link's
    /// or update_link's <c>pixel_snippet</c> argument to keep the same pixel.</summary>
    private sealed record LinkMetadataResult(
        string? UtmSource, string? UtmMedium, string? UtmCampaign, string? UtmTerm, string? UtmContent,
        string? PixelSnippet, string? PixelValue, string? IosDeepLink, string? AndroidDeepLink);
    private sealed record PixelSnippetItem(string Name, bool IsCustom);
    private sealed record LinkStats(
        string ShortCode,
        string? Domain,
        string LongUrl,
        long ClickCount,
        string? Title,
        string? Description,
        IReadOnlyList<string> Tags,
        DateTime? ArchivedAtUtc,
        LinkMetadataResult? Metadata,
        IReadOnlyList<NameCount> TopReferrers,
        IReadOnlyList<NameCount> TopCountries,
        IReadOnlyList<NameCount> TopDevices,
        IReadOnlyList<NameCount> TopBrowsers);
}
