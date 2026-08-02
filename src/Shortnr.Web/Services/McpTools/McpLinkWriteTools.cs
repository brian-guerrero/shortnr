using System.ComponentModel;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Web.Models;
using Shortnr.Web.Services;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Shortnr.Web.Services.McpTools;

/// <summary>
/// Write tools for short links: create, update, and delete. Every tool requires
/// the <c>mcp:write</c> scope. Destructive actions go through MRTR confirmation
/// when the client supports it, otherwise they demand an explicit
/// <c>confirmed=true</c> argument. Each mutation is audited via
/// <see cref="AiActivityProcessor"/>.
/// </summary>
[McpServerToolType]
public static class McpLinkWriteTools
{
    [McpServerTool(Name = "create_short_link", Title = "Create a short link")]
    public static async Task<string> CreateShortLink(
        RequestContext<CallToolRequestParams> context,
        AppDbContext db,
        UserIdentityService identity,
        Channel<AiActivityRecord> activity,
        [Description("The destination URL (http or https)")] string url,
        [Description("Optional vanity slug: 1-64 chars, letters/digits/'-'/'_', starting with a letter or digit")] string? custom_slug = null,
        [Description("Optional verified domain hostname, 'default' for the instance host, or omit to use your default domain")] string? domain = null,
        CancellationToken ct = default)
    {
        var ownerUserId = await McpToolGuard.ResolveOwnerAsync(context, identity);
        if (ownerUserId is null) return McpToolGuard.OwnerError;
        if (!McpToolGuard.HasScope(context, ApiKeyScopes.McpWrite)) return McpToolGuard.WriteScopeError;

        var target = url.Trim();
        if (!Uri.TryCreate(target, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
            return "Error: url must be an absolute http(s) URL.";

        long? domainId = null;
        if (!string.IsNullOrWhiteSpace(domain))
        {
            var hostname = domain.Trim().ToLowerInvariant();
            if (hostname != "default")
            {
                var resolved = await db.Domains.FirstOrDefaultAsync(
                    d => d.Hostname == hostname && d.OwnerUserId == ownerUserId && d.IsVerified, ct);
                if (resolved is null)
                    return $"Error: '{hostname}' is not a verified domain owned by this account.";
                domainId = resolved.Id;
            }
        }
        else
        {
            domainId = await db.Domains
                .Where(d => d.OwnerUserId == ownerUserId && d.IsVerified && d.IsDefault)
                .Select(d => (long?)d.Id)
                .FirstOrDefaultAsync(ct);
        }

        string shortCode;
        var slug = custom_slug?.Trim() ?? "";
        if (slug.Length > 0)
        {
            if (!ShortLinkCodes.IsValidSlug(slug))
                return "Error: custom_slug must be 1-64 chars: letters, digits, '-' or '_', starting with a letter or digit.";
            if (await db.ShortenedUrls.AnyAsync(l => l.DomainId == domainId && l.ShortCode == slug, ct))
                return $"Error: the short code '{slug}' is already in use on this domain.";
            shortCode = slug;
        }
        else
        {
            shortCode = await ShortLinkCodes.GenerateUniqueCodeAsync(
                code => db.ShortenedUrls.AnyAsync(l => l.DomainId == domainId && l.ShortCode == code, ct));
        }

        var link = new ShortenedUrl
        {
            LongUrl = target,
            ShortCode = shortCode,
            DomainId = domainId,
            OwnerUserId = ownerUserId.Value,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.ShortenedUrls.Add(link);
        await db.SaveChangesAsync(ct);

        McpToolGuard.LogActivity(activity, ownerUserId.Value, McpToolGuard.ResolveApiKeyId(context),
            "create_short_link", nameof(ShortenedUrl), link.Id,
            $"Created short link '{shortCode}' pointing to {target}");

        return McpToolGuard.Json(new LinkResult(shortCode, link.Domain?.Hostname, target, 0));
    }

    [McpServerTool(Name = "update_link", Title = "Update a short link")]
    public static async Task<string> UpdateLink(
        McpServer server,
        RequestContext<CallToolRequestParams> context,
        AppDbContext db,
        UserIdentityService identity,
        Channel<AiActivityRecord> activity,
        [Description("The short code of the link to update")] string short_code,
        [Description("New destination URL")] string? url = null,
        [Description("New vanity slug")] string? slug = null,
        [Description("New domain hostname, or 'default' to move to the instance host; omit to keep the current domain")] string? domain = null,
        [Description("Explicit confirmation: required when changing the destination of a link that already has clicks or a bio-page placement")] bool? confirmed = null,
        CancellationToken ct = default)
    {
        var ownerUserId = await McpToolGuard.ResolveOwnerAsync(context, identity);
        if (ownerUserId is null) return McpToolGuard.OwnerError;
        if (!McpToolGuard.HasScope(context, ApiKeyScopes.McpWrite)) return McpToolGuard.WriteScopeError;

        var code = short_code.Trim();
        var link = await McpToolGuard.ResolveOwnedLinkAsync(db, ownerUserId.Value, code, ct);
        if (link is null)
            return $"Error: no link with short code '{code}' found.";

        var changes = new List<string>();
        var resultDomain = link.Domain?.Hostname;
        var onBioPage = await db.BioPageLinks.AnyAsync(b => b.ShortenedUrlId == link.Id, ct);

        if (url is not null)
        {
            var newUrl = url.Trim();
            if (!Uri.TryCreate(newUrl, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
                return "Error: url must be an absolute http(s) URL.";
            if (newUrl != link.LongUrl)
            {
                if (link.ClickCount > 0 || onBioPage)
                {
                    var confirmation = McpToolGuard.ResolveConfirmation(server, context, "confirmUpdate",
                        $"Change '{link.ShortCode}' destination to {newUrl}? It has {link.ClickCount} clicks" +
                        (onBioPage ? " and is currently on your bio page" : "") +
                        " — this affects a link that may already be shared.",
                        code, confirmed);
                    if (confirmation == McpToolGuard.Confirmation.NeedsConfirmation)
                        return "Pass confirmed=true to change the destination of this link. It already has clicks or a bio-page placement and this change is not easily reversible.";
                    if (confirmation == McpToolGuard.Confirmation.Declined)
                        return "Update cancelled.";
                }
                link.LongUrl = newUrl;
                changes.Add($"destination to {newUrl}");
            }
        }

        if (slug is not null)
        {
            var newSlug = slug.Trim();
            if (newSlug.Length == 0 || !ShortLinkCodes.IsValidSlug(newSlug))
                return "Error: slug must be 1-64 chars: letters, digits, '-' or '_', starting with a letter or digit.";
            if (newSlug != link.ShortCode)
            {
                var collides = await db.ShortenedUrls.AnyAsync(
                    l => l.Id != link.Id && l.DomainId == link.DomainId && l.ShortCode == newSlug, ct);
                if (collides)
                    return $"Error: the short code '{newSlug}' is already in use on this domain.";
                link.ShortCode = newSlug;
                changes.Add($"short code to '{newSlug}'");
            }
        }

        if (domain is not null)
        {
            var hostname = domain.Trim().ToLowerInvariant();
            long? newDomainId;
            if (hostname.Length == 0 || hostname == "default")
            {
                newDomainId = null;
                resultDomain = null;
            }
            else
            {
                var resolved = await db.Domains.FirstOrDefaultAsync(
                    d => d.Hostname == hostname && d.OwnerUserId == ownerUserId && d.IsVerified, ct);
                if (resolved is null)
                    return $"Error: '{hostname}' is not a verified domain owned by this account.";
                newDomainId = resolved.Id;
                resultDomain = resolved.Hostname;
            }
            if (newDomainId != link.DomainId)
            {
                var collides = await db.ShortenedUrls.AnyAsync(
                    l => l.Id != link.Id && l.DomainId == newDomainId && l.ShortCode == link.ShortCode, ct);
                if (collides)
                    return "Error: that short code is already in use on the destination domain.";
                link.DomainId = newDomainId;
                changes.Add("domain");
            }
        }

        if (changes.Count == 0)
            return "No changes were requested or needed.";

        await db.SaveChangesAsync(ct);

        McpToolGuard.LogActivity(activity, ownerUserId.Value, McpToolGuard.ResolveApiKeyId(context),
            "update_link", nameof(ShortenedUrl), link.Id,
            $"Updated short link '{code}': {string.Join(", ", changes)}");

        return McpToolGuard.Json(new LinkResult(link.ShortCode, resultDomain, link.LongUrl, link.ClickCount));
    }

    [McpServerTool(Name = "delete_link", Title = "Delete a short link")]
    public static async Task<string> DeleteLink(
        McpServer server,
        RequestContext<CallToolRequestParams> context,
        AppDbContext db,
        UserIdentityService identity,
        Channel<AiActivityRecord> activity,
        [Description("The short code of the link to delete")] string short_code,
        [Description("Explicit confirmation: this permanently removes the link and cannot be undone")] bool? confirmed = null,
        CancellationToken ct = default)
    {
        var ownerUserId = await McpToolGuard.ResolveOwnerAsync(context, identity);
        if (ownerUserId is null) return McpToolGuard.OwnerError;
        if (!McpToolGuard.HasScope(context, ApiKeyScopes.McpWrite)) return McpToolGuard.WriteScopeError;

        var code = short_code.Trim();
        var link = await McpToolGuard.ResolveOwnedLinkAsync(db, ownerUserId.Value, code, ct);
        if (link is null)
            return $"Error: no link with short code '{code}' found.";

        var onBioPage = await db.BioPageLinks.AnyAsync(b => b.ShortenedUrlId == link.Id, ct);
        var confirmation = McpToolGuard.ResolveConfirmation(server, context, "confirmDelete",
            $"Delete short link '{link.ShortCode}' ({link.ClickCount} clicks{(onBioPage ? ", currently on your bio page" : "")})? This cannot be undone.",
            code, confirmed);

        if (confirmation == McpToolGuard.Confirmation.NeedsConfirmation)
            return "Pass confirmed=true to delete this link. This action cannot be undone.";
        if (confirmation == McpToolGuard.Confirmation.Declined)
            return "Deletion cancelled.";

        var deletedCode = link.ShortCode;
        db.ShortenedUrls.Remove(link);
        await db.SaveChangesAsync(ct);

        McpToolGuard.LogActivity(activity, ownerUserId.Value, McpToolGuard.ResolveApiKeyId(context),
            "delete_link", nameof(ShortenedUrl), link.Id,
            $"Deleted short link '{deletedCode}'");

        return $"Deleted short link '{deletedCode}'.";
    }

    private sealed record LinkResult(string ShortCode, string? Domain, string LongUrl, long ClickCount);
}
