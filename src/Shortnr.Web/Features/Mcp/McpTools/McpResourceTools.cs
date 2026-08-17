using System.ComponentModel;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Shortnr.Web.Features.Mcp.McpTools;

/// <summary>
/// Read-only MCP resources exposing shortnr state as structured data: paginated
/// link lists, single-link metadata, click analytics, and workspaces. Resources
/// complement the tools — clients can read state without consuming a tool call —
/// and are read-only by design; mutations go through tools. Every resource
/// requires the <c>mcp:read</c> scope.
/// </summary>
[McpServerResourceType]
public static class McpResourceTools
{
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;

    /// <summary>Lists every short link the caller can access, newest first. Filterable
    /// by workspace slug and tag; paginated with <c>limit</c>/<c>offset</c> query params.</summary>
    [McpServerResource(
        UriTemplate = "shortnr://links{?limit,offset,workspace,tag}",
        Name = "links",
        Title = "Short links",
        MimeType = "application/json")]
    [Description("List of short links the authenticated user can access (personal plus workspace links), paginated and filterable by workspace slug and tag.")]
    public static async Task<string> LinksResource(
        RequestContext<ReadResourceRequestParams> context,
        AppDbContext db,
        UserIdentityService identity,
        string? limit = null,
        string? offset = null,
        string? workspace = null,
        string? tag = null,
        CancellationToken ct = default)
    {
        var ownerUserId = await McpToolGuard.ResolveOwnerAsync(context, identity);
        if (ownerUserId is null) return McpToolGuard.JsonError("Authentication required — no owner could be resolved for this API key.");
        if (!McpToolGuard.HasScope(context, ApiKeyScopes.McpRead))
            return McpToolGuard.JsonError($"This resource requires the '{ApiKeyScopes.McpRead}' scope.");

        var pageSize = int.TryParse(limit, out var parsedLimit)
            ? Math.Clamp(parsedLimit, 1, MaxPageSize)
            : DefaultPageSize;
        var pageOffset = int.TryParse(offset, out var parsedOffset) && parsedOffset > 0 ? parsedOffset : 0;

        var query = McpToolGuard.AccessibleLinksQuery(db, ownerUserId.Value);
        if (!string.IsNullOrWhiteSpace(workspace))
        {
            var slug = workspace.Trim();
            query = query.Where(l => l.Workspace != null && l.Workspace.Slug == slug);
        }
        if (!string.IsNullOrWhiteSpace(tag))
        {
            var name = tag.Trim();
            query = query.Where(l => l.Tags.Any(t => t.Name == name));
        }

        var total = await query.CountAsync(ct);
        var links = await query
            .Include(l => l.Domain)
            .Include(l => l.Tags)
            .Include(l => l.Workspace)
            .OrderByDescending(l => l.CreatedAtUtc)
            .Skip(pageOffset)
            .Take(pageSize)
            .Select(l => new LinkResourceItem(
                l.ShortCode,
                l.Domain != null ? l.Domain.Hostname : null,
                l.LongUrl,
                l.ClickCount,
                l.CreatedAtUtc,
                l.Title,
                l.Description,
                l.Tags.OrderBy(t => t.Name).Select(t => t.Name).ToList(),
                l.ArchivedAtUtc,
                l.Workspace != null ? l.Workspace.Slug : null))
            .ToListAsync(ct);

        string? next = pageOffset + links.Count < total
            ? $"shortnr://links?limit={pageSize}&offset={pageOffset + links.Count}"
            : null;

        return McpToolGuard.Json(new LinksResourceResult(total, pageSize, pageOffset, next, links));
    }

    /// <summary>Reads a single link's metadata by short code: destination, domain,
    /// tags, timestamps, click count, and campaign metadata.</summary>
    [McpServerResource(
        UriTemplate = "shortnr://links/{code}",
        Name = "link",
        Title = "Short link metadata",
        MimeType = "application/json")]
    [Description("Metadata for a single short link: destination, domain, tags, created/updated/archived timestamps, click count and campaign metadata.")]
    public static async Task<string> LinkResource(
        RequestContext<ReadResourceRequestParams> context,
        AppDbContext db,
        UserIdentityService identity,
        WorkspaceService workspaceService,
        string code,
        CancellationToken ct = default)
    {
        var ownerUserId = await McpToolGuard.ResolveOwnerAsync(context, identity);
        if (ownerUserId is null) return McpToolGuard.JsonError("Authentication required — no owner could be resolved for this API key.");
        if (!McpToolGuard.HasScope(context, ApiKeyScopes.McpRead))
            return McpToolGuard.JsonError($"This resource requires the '{ApiKeyScopes.McpRead}' scope.");

        if (string.IsNullOrWhiteSpace(code))
            return McpToolGuard.JsonError("A short code is required.");

        var link = await McpToolGuard.ResolveAccessibleLinkAsync(db, ownerUserId.Value, code.Trim(), workspaceService, ct);
        if (link is null)
            return McpToolGuard.JsonError($"No link with short code '{code}' found.");

        var tags = await db.ShortenedUrlTags
            .Where(t => t.ShortenedUrlId == link.Id)
            .OrderBy(t => t.Name)
            .Select(t => t.Name)
            .ToListAsync(ct);

        return McpToolGuard.Json(new LinkResourceItem(
            link.ShortCode,
            link.Domain?.Hostname,
            link.LongUrl,
            link.ClickCount,
            link.CreatedAtUtc,
            link.Title,
            link.Description,
            tags,
            link.ArchivedAtUtc,
            link.Workspace?.Slug,
            link.Metadata is null ? null : new LinkMetadataResult(
                link.Metadata.UtmSource, link.Metadata.UtmMedium, link.Metadata.UtmCampaign,
                link.Metadata.UtmTerm, link.Metadata.UtmContent,
                link.Metadata.PixelSnippet?.Name, link.Metadata.PixelId,
                link.Metadata.IosDeepLink, link.Metadata.AndroidDeepLink)));
    }

    /// <summary>Reads click analytics for a link: total, daily timeline, and top
    /// referrers/devices/browsers/countries. Optionally constrained to a
    /// <c>from</c>/<c>to</c> date range (yyyy-MM-dd, inclusive).</summary>
    [McpServerResource(
        UriTemplate = "shortnr://analytics/{code}{?from,to}",
        Name = "link-analytics",
        Title = "Short link analytics",
        MimeType = "application/json")]
    [Description("Click analytics for a short link: total clicks, daily timeline, and top referrers, devices, browsers and countries, optionally filtered by date range.")]
    public static async Task<string> LinkAnalyticsResource(
        RequestContext<ReadResourceRequestParams> context,
        AppDbContext db,
        UserIdentityService identity,
        WorkspaceService workspaceService,
        string code,
        string? from = null,
        string? to = null,
        CancellationToken ct = default)
    {
        var ownerUserId = await McpToolGuard.ResolveOwnerAsync(context, identity);
        if (ownerUserId is null) return McpToolGuard.JsonError("Authentication required — no owner could be resolved for this API key.");
        if (!McpToolGuard.HasScope(context, ApiKeyScopes.McpRead))
            return McpToolGuard.JsonError($"This resource requires the '{ApiKeyScopes.McpRead}' scope.");

        if (string.IsNullOrWhiteSpace(code))
            return McpToolGuard.JsonError("A short code is required.");

        var fromValue = ParseDate(from);
        if (fromValue.Error is not null)
            return McpToolGuard.JsonError(fromValue.Error);
        var toValue = ParseDate(to);
        if (toValue.Error is not null)
            return McpToolGuard.JsonError(toValue.Error);

        var link = await McpToolGuard.ResolveAccessibleLinkAsync(db, ownerUserId.Value, code.Trim(), workspaceService, ct);
        if (link is null)
            return McpToolGuard.JsonError($"No link with short code '{code}' found.");

        var clickQuery = db.ClickEvents.Where(e => e.ShortenedUrlId == link.Id);
        if (fromValue.Value is not null)
            clickQuery = clickQuery.Where(e => e.ClickedAtUtc >= fromValue.Value);
        if (toValue.Value is not null)
            clickQuery = clickQuery.Where(e => e.ClickedAtUtc < toValue.Value.Value.AddDays(1));

        var total = await clickQuery.CountAsync(ct);

        var timeline = (await clickQuery
            .GroupBy(e => e.ClickedAtUtc.Date)
            .Select(g => new { Date = g.Key, Count = g.Count() })
            .OrderBy(x => x.Date)
            .ToListAsync(ct))
            .Select(x => new DateCount(x.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), x.Count))
            .ToList();

        var referrers = await GroupCountsAsync(clickQuery
            .Where(e => e.Referer != "")
            .GroupBy(e => e.Referer), ct);
        var devices = await GroupCountsAsync(clickQuery
            .Where(e => e.DeviceFamily != null && e.DeviceFamily != "")
            .GroupBy(e => e.DeviceFamily!), ct);
        var browsers = await GroupCountsAsync(clickQuery
            .Where(e => e.Browser != null && e.Browser != "")
            .GroupBy(e => e.Browser!), ct);
        var geo = await GroupCountsAsync(clickQuery
            .Where(e => e.CountryName != null)
            .GroupBy(e => e.CountryName!), ct);

        return McpToolGuard.Json(new LinkAnalyticsResourceResult(
            link.ShortCode, link.Domain?.Hostname, link.LongUrl, total,
            from?.Trim(), to?.Trim(),
            timeline, referrers, devices, browsers, geo));
    }

    /// <summary>Lists the workspaces the authenticated user is a member of, with
    /// their role, member count, and number of links in each workspace.</summary>
    [McpServerResource(
        UriTemplate = "shortnr://workspaces",
        Name = "workspaces",
        Title = "Workspaces",
        MimeType = "application/json")]
    [Description("List of workspaces the authenticated user can access, with the caller's role, member count and link count per workspace.")]
    public static async Task<string> WorkspacesResource(
        RequestContext<ReadResourceRequestParams> context,
        AppDbContext db,
        UserIdentityService identity,
        CancellationToken ct = default)
    {
        var ownerUserId = await McpToolGuard.ResolveOwnerAsync(context, identity);
        if (ownerUserId is null) return McpToolGuard.JsonError("Authentication required — no owner could be resolved for this API key.");
        if (!McpToolGuard.HasScope(context, ApiKeyScopes.McpRead))
            return McpToolGuard.JsonError($"This resource requires the '{ApiKeyScopes.McpRead}' scope.");

        var memberships = await db.WorkspaceMembers
            .Where(m => m.UserId == ownerUserId && m.JoinedAtUtc != null)
            .Select(m => new { m.WorkspaceId, m.Role, Workspace = m.Workspace! })
            .OrderBy(x => x.Workspace.Name)
            .ToListAsync(ct);

        if (memberships.Count == 0)
            return McpToolGuard.Json(new WorkspacesResourceResult([]));

        var workspaceIds = memberships.Select(m => m.WorkspaceId).ToList();
        var memberCounts = await db.WorkspaceMembers
            .Where(m => workspaceIds.Contains(m.WorkspaceId) && m.JoinedAtUtc != null)
            .GroupBy(m => m.WorkspaceId)
            .Select(g => new { WorkspaceId = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var linkCounts = await db.ShortenedUrls
            .Where(l => l.WorkspaceId != null && workspaceIds.Contains(l.WorkspaceId.Value))
            .GroupBy(l => l.WorkspaceId!.Value)
            .Select(g => new { WorkspaceId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var memberCountMap = memberCounts.ToDictionary(x => x.WorkspaceId, x => x.Count);
        var linkCountMap = linkCounts.ToDictionary(x => x.WorkspaceId, x => x.Count);

        var items = memberships.Select(m => new WorkspaceResourceItem(
            m.Workspace.Slug,
            m.Workspace.Name,
            m.Role.ToString(),
            memberCountMap.GetValueOrDefault(m.WorkspaceId),
            linkCountMap.GetValueOrDefault(m.WorkspaceId),
            m.Workspace.CreatedAtUtc)).ToList();

        return McpToolGuard.Json(new WorkspacesResourceResult(items));
    }

    private static (DateTime? Value, string? Error) ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return (null, null);
        if (DateTime.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var date))
            return (date, null);
        return (null, $"Invalid date '{value}'. Expected yyyy-MM-dd.");
    }

    private static async Task<List<NameCount>> GroupCountsAsync(IQueryable<IGrouping<string, ClickEvent>> grouped, CancellationToken ct)
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

    private sealed record LinkResourceItem(
        string ShortCode, string? Domain, string LongUrl, long ClickCount, DateTime CreatedAtUtc,
        string? Title = null, string? Description = null, IReadOnlyList<string>? Tags = null,
        DateTime? ArchivedAtUtc = null, string? Workspace = null, LinkMetadataResult? Metadata = null);

    private sealed record LinkMetadataResult(
        string? UtmSource, string? UtmMedium, string? UtmCampaign, string? UtmTerm, string? UtmContent,
        string? PixelSnippet, string? PixelValue, string? IosDeepLink, string? AndroidDeepLink);

    private sealed record LinksResourceResult(
        int Total, int Limit, int Offset, string? Next, IReadOnlyList<LinkResourceItem> Links);

    private sealed record DateCount(string Date, long Count);
    private sealed record NameCount(string Name, long Count);

    private sealed record LinkAnalyticsResourceResult(
        string ShortCode, string? Domain, string LongUrl, long Total,
        string? From, string? To,
        IReadOnlyList<DateCount> Timeline,
        IReadOnlyList<NameCount> Referrers,
        IReadOnlyList<NameCount> Devices,
        IReadOnlyList<NameCount> Browsers,
        IReadOnlyList<NameCount> Geo);

    private sealed record WorkspaceResourceItem(
        string Slug, string Name, string Role, int MemberCount, int LinkCount, DateTime CreatedAtUtc);

    private sealed record WorkspacesResourceResult(IReadOnlyList<WorkspaceResourceItem> Workspaces);
}
