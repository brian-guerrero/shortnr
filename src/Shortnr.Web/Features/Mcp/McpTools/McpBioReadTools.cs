using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Shortnr.Web.Features.Mcp.McpTools;

/// <summary>
/// Read-only bio-page tools and resources. <c>list_bio_page_links</c> reports the
/// current link layout; the <c>shortnr://bio</c> resource lets a client inspect the
/// same state without consuming a tool call. Both require the <c>mcp:read</c> scope.
/// </summary>
public static class McpBioReadTools
{
    [McpServerToolType]
    [McpServerResourceType]
    public static class BioTools
    {
        [McpServerTool(Name = "list_bio_page_links", Title = "List bio page links", ReadOnly = true)]
        public static async Task<string> ListBioPageLinks(
            RequestContext<CallToolRequestParams> context,
            AppDbContext db,
            UserIdentityService identity,
            CancellationToken ct = default)
        {
            var ownerUserId = await McpToolGuard.ResolveOwnerAsync(context, identity);
            if (ownerUserId is null) return McpToolGuard.OwnerError;
            if (!McpToolGuard.HasScope(context, ApiKeyScopes.McpRead)) return McpToolGuard.ReadScopeError;

            var state = await LoadBioPageStateAsync(db, ownerUserId.Value, ct);
            return state is null
                ? "No bio page exists yet. Use the dashboard or create_short_link + add_link_to_bio_page to build one."
                : McpToolGuard.Json(state);
        }

        [McpServerResource(
            UriTemplate = "shortnr://bio",
            Name = "bio-page",
            Title = "Current bio page",
            MimeType = "application/json")]
        public static async Task<string> BioPageResource(
            RequestContext<ReadResourceRequestParams> context,
            AppDbContext db,
            UserIdentityService identity,
            CancellationToken ct = default)
        {
            var ownerUserId = await McpToolGuard.ResolveOwnerAsync(context, identity);
            if (ownerUserId is null)
                return "{\"error\":\"Authentication required — no owner could be resolved for this API key.\"}";
            if (!McpToolGuard.HasScope(context, ApiKeyScopes.McpRead))
                return $"{{\"error\":\"This resource requires the '{ApiKeyScopes.McpRead}' scope.\"}}";

            var state = await LoadBioPageStateAsync(db, ownerUserId.Value, ct);
            return state is null
                ? "{\"error\":\"No bio page exists yet.\"}"
                : McpToolGuard.Json(state);
        }
    }

    /// <summary>Loads the owner's bio page with ordered links, or null when none exists.</summary>
    public static async Task<BioPageState?> LoadBioPageStateAsync(AppDbContext db, long ownerUserId, CancellationToken ct)
    {
        var bioPage = await db.BioPages
            .Include(b => b.Links)
                .ThenInclude(l => l.ShortenedUrl)
                    .ThenInclude(s => s!.Domain)
            .FirstOrDefaultAsync(b => b.OwnerUserId == ownerUserId, ct);
        if (bioPage is null) return null;

        var links = bioPage.Links
            .OrderBy(l => l.SortOrder)
            .ThenBy(l => l.Id)
            .Select(l => new BioPageLinkItem(
                l.SortOrder + 1,
                l.ShortenedUrl!.ShortCode,
                l.ShortenedUrl.LongUrl,
                l.ShortenedUrl.Domain?.Hostname,
                l.Title,
                l.IsVisible))
            .ToList();

        return new BioPageState(
            bioPage.Slug,
            bioPage.DisplayName,
            bioPage.Theme,
            bioPage.BioText,
            bioPage.AvatarUrl,
            links);
    }

    public sealed record BioPageState(
        string Slug,
        string DisplayName,
        string Theme,
        string? BioText,
        string? AvatarUrl,
        IReadOnlyList<BioPageLinkItem> Links);

    public sealed record BioPageLinkItem(
        int Position,
        string ShortCode,
        string LongUrl,
        string? Domain,
        string Title,
        bool IsVisible);
}
