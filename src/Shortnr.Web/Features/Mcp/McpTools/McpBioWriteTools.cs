using System.ComponentModel;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Shortnr.Web.Features.Mcp.McpTools;

/// <summary>
/// Write tools for the owner's bio page: adding/removing links, reordering, and
/// theme changes. Every tool requires the <c>mcp:write</c> scope and audits its
/// mutation via <see cref="AiActivityProcessor"/>.
/// </summary>
public static class McpBioWriteTools
{
    [McpServerToolType]
    public static class BioTools
    {
        [McpServerTool(Name = "add_link_to_bio_page", Title = "Add a link to the bio page")]
        public static async Task<string> AddLinkToBioPage(
            RequestContext<CallToolRequestParams> context,
            AppDbContext db,
            UserIdentityService identity,
            Channel<AiActivityRecord> activity,
            [Description("The short code of the link to add")] string short_code,
            [Description("1-based position to insert at; omit to append")] int? position = null,
            [Description("Optional display title; defaults to the short code")] string? title = null,
            CancellationToken ct = default)
        {
            var ownerUserId = await McpToolGuard.ResolveOwnerAsync(context, identity);
            if (ownerUserId is null) return McpToolGuard.OwnerError;
            if (!McpToolGuard.HasScope(context, ApiKeyScopes.McpWrite)) return McpToolGuard.WriteScopeError;

            var bioPage = await db.BioPages.FirstOrDefaultAsync(b => b.OwnerUserId == ownerUserId, ct);
            if (bioPage is null)
                return "Error: no bio page exists yet. Create one on the dashboard or via the web UI first.";

            var link = await McpToolGuard.ResolveOwnedLinkAsync(db, ownerUserId.Value, short_code.Trim(), ct);
            if (link is null)
                return $"Error: no link with short code '{short_code}' found.";

            if (await db.BioPageLinks.AnyAsync(b => b.BioPageId == bioPage.Id && b.ShortenedUrlId == link.Id, ct))
                return "Error: that link is already on your bio page.";

            var existing = await db.BioPageLinks
                .Where(b => b.BioPageId == bioPage.Id)
                .OrderBy(b => b.SortOrder)
                .ThenBy(b => b.Id)
                .ToListAsync(ct);

            int insertIndex;
            if (position is null or <= 0)
            {
                insertIndex = existing.Count;
            }
            else if (position.Value > existing.Count + 1)
            {
                return $"Error: position must be between 1 and {existing.Count + 1}.";
            }
            else
            {
                insertIndex = position.Value - 1;
            }

            var titleText = (title ?? "").Trim();
            db.BioPageLinks.Add(new BioPageLink
            {
                BioPageId = bioPage.Id,
                ShortenedUrlId = link.Id,
                Title = titleText.Length > 0 ? titleText : link.ShortCode,
                SortOrder = existing.Count,
                IsVisible = true
            });
            await db.SaveChangesAsync(ct);

            var ordered = await db.BioPageLinks
                .Where(b => b.BioPageId == bioPage.Id)
                .OrderBy(b => b.SortOrder)
                .ThenBy(b => b.Id)
                .ToListAsync(ct);
            var moved = ordered.First(b => b.ShortenedUrlId == link.Id);
            ordered.Remove(moved);
            ordered.Insert(Math.Min(insertIndex, ordered.Count), moved);
            for (var i = 0; i < ordered.Count; i++)
                ordered[i].SortOrder = i;
            await db.SaveChangesAsync(ct);

            McpToolGuard.LogActivity(activity, ownerUserId.Value, McpToolGuard.ResolveApiKeyId(context),
                "add_link_to_bio_page", nameof(BioPage), bioPage.Id,
                $"Added '{link.ShortCode}' to bio page position {insertIndex + 1}");

            return $"Added '{link.ShortCode}' to your bio page at position {insertIndex + 1}.";
        }

        [McpServerTool(Name = "remove_link_from_bio_page", Title = "Remove a link from the bio page")]
        public static async Task<string> RemoveLinkFromBioPage(
            RequestContext<CallToolRequestParams> context,
            AppDbContext db,
            UserIdentityService identity,
            Channel<AiActivityRecord> activity,
            [Description("The short code of the link to remove")] string short_code,
            CancellationToken ct = default)
        {
            var ownerUserId = await McpToolGuard.ResolveOwnerAsync(context, identity);
            if (ownerUserId is null) return McpToolGuard.OwnerError;
            if (!McpToolGuard.HasScope(context, ApiKeyScopes.McpWrite)) return McpToolGuard.WriteScopeError;

            var bioPage = await db.BioPages.FirstOrDefaultAsync(b => b.OwnerUserId == ownerUserId, ct);
            if (bioPage is null)
                return "Error: no bio page exists yet.";

            var entry = await db.BioPageLinks
                .FirstOrDefaultAsync(b => b.BioPageId == bioPage.Id && b.ShortenedUrl!.ShortCode == short_code.Trim(), ct);
            if (entry is null)
                return $"Error: no link with short code '{short_code}' is on your bio page.";

            db.BioPageLinks.Remove(entry);
            await db.SaveChangesAsync(ct);

            var remaining = await db.BioPageLinks
                .Where(b => b.BioPageId == bioPage.Id)
                .OrderBy(b => b.SortOrder)
                .ThenBy(b => b.Id)
                .ToListAsync(ct);
            for (var i = 0; i < remaining.Count; i++)
                remaining[i].SortOrder = i;
            await db.SaveChangesAsync(ct);

            McpToolGuard.LogActivity(activity, ownerUserId.Value, McpToolGuard.ResolveApiKeyId(context),
                "remove_link_from_bio_page", nameof(BioPage), bioPage.Id,
                $"Removed '{short_code.Trim()}' from bio page");

            return $"Removed '{short_code.Trim()}' from your bio page.";
        }

        [McpServerTool(Name = "reorder_bio_page", Title = "Reorder bio page links")]
        public static async Task<string> ReorderBioPage(
            RequestContext<CallToolRequestParams> context,
            AppDbContext db,
            UserIdentityService identity,
            Channel<AiActivityRecord> activity,
            [Description("The short codes in their new top-to-bottom order; must include every link on the page")] string[] order,
            CancellationToken ct = default)
        {
            var ownerUserId = await McpToolGuard.ResolveOwnerAsync(context, identity);
            if (ownerUserId is null) return McpToolGuard.OwnerError;
            if (!McpToolGuard.HasScope(context, ApiKeyScopes.McpWrite)) return McpToolGuard.WriteScopeError;

            var codes = order.Select(c => c.Trim()).ToList();
            if (codes.Count == 0)
                return "Error: order must contain at least one short code.";

            var bioPage = await db.BioPages.FirstOrDefaultAsync(b => b.OwnerUserId == ownerUserId, ct);
            if (bioPage is null)
                return "Error: no bio page exists yet.";

            var entries = await db.BioPageLinks
                .Include(b => b.ShortenedUrl)
                .Where(b => b.BioPageId == bioPage.Id)
                .ToListAsync(ct);

            var byCode = entries.ToDictionary(b => b.ShortenedUrl!.ShortCode, b => b);
            var invalid = codes.Where(c => !byCode.ContainsKey(c)).ToList();
            if (invalid.Count > 0)
                return $"Error: these short codes are not on your bio page: {string.Join(", ", invalid)}.";
            if (codes.Count != byCode.Count)
                return "Error: order must list every link currently on your bio page.";

            for (var i = 0; i < codes.Count; i++)
                byCode[codes[i]].SortOrder = i;
            await db.SaveChangesAsync(ct);

            McpToolGuard.LogActivity(activity, ownerUserId.Value, McpToolGuard.ResolveApiKeyId(context),
                "reorder_bio_page", nameof(BioPage), bioPage.Id,
                $"Reordered bio page links: {string.Join(", ", codes)}");

            return $"Reordered your bio page links: {string.Join(", ", codes)}.";
        }

        [McpServerTool(Name = "set_bio_page_theme", Title = "Set the bio page theme")]
        public static async Task<string> SetBioPageTheme(
            RequestContext<CallToolRequestParams> context,
            AppDbContext db,
            UserIdentityService identity,
            Channel<AiActivityRecord> activity,
            [Description("One of the preset themes")] string theme,
            CancellationToken ct = default)
        {
            var ownerUserId = await McpToolGuard.ResolveOwnerAsync(context, identity);
            if (ownerUserId is null) return McpToolGuard.OwnerError;
            if (!McpToolGuard.HasScope(context, ApiKeyScopes.McpWrite)) return McpToolGuard.WriteScopeError;

            var normalized = theme.Trim().ToLowerInvariant();
            if (!BioThemes.IsValid(normalized))
                return $"Error: unknown theme '{theme}'. Valid themes: {string.Join(", ", BioThemes.All)}.";

            var bioPage = await db.BioPages.FirstOrDefaultAsync(b => b.OwnerUserId == ownerUserId, ct);
            if (bioPage is null)
                return "Error: no bio page exists yet.";

            bioPage.Theme = normalized;
            await db.SaveChangesAsync(ct);

            McpToolGuard.LogActivity(activity, ownerUserId.Value, McpToolGuard.ResolveApiKeyId(context),
                "set_bio_page_theme", nameof(BioPage), bioPage.Id,
                $"Set bio page theme to '{normalized}'");

            return $"Bio page theme is now '{normalized}'.";
        }

        [McpServerTool(Name = "set_bio_page_text", Title = "Set the bio page text")]
        public static async Task<string> SetBioPageText(
            RequestContext<CallToolRequestParams> context,
            AppDbContext db,
            UserIdentityService identity,
            Channel<AiActivityRecord> activity,
            [Description("The bio text shown on the page; empty string clears it")] string text,
            CancellationToken ct = default)
        {
            var ownerUserId = await McpToolGuard.ResolveOwnerAsync(context, identity);
            if (ownerUserId is null) return McpToolGuard.OwnerError;
            if (!McpToolGuard.HasScope(context, ApiKeyScopes.McpWrite)) return McpToolGuard.WriteScopeError;

            var normalized = text.Trim();
            if (normalized.Length > 2000)
                return "Error: bio text must be 2000 characters or fewer.";

            var bioPage = await db.BioPages.FirstOrDefaultAsync(b => b.OwnerUserId == ownerUserId, ct);
            if (bioPage is null)
                return "Error: no bio page exists yet.";

            bioPage.BioText = normalized.Length > 0 ? normalized : null;
            await db.SaveChangesAsync(ct);

            McpToolGuard.LogActivity(activity, ownerUserId.Value, McpToolGuard.ResolveApiKeyId(context),
                "set_bio_page_text", nameof(BioPage), bioPage.Id,
                normalized.Length > 0 ? $"Set bio page text ({normalized.Length} chars)" : "Cleared bio page text");

            return normalized.Length > 0
                ? "Bio page text updated."
                : "Bio page text cleared.";
        }
    }
}
