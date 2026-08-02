using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Web.Services;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Shortnr.Web.Services.McpTools;

/// <summary>
/// Shared plumbing for MCP tools: scope checks against the authenticated
/// principal, owner resolution, owned-link lookup, and JSON output formatting.
/// </summary>
public static class McpToolGuard
{
    public const string OwnerError = "Error: authentication required — no owner could be resolved for this API key.";
    public const string ReadScopeError = $"Error: this tool requires the '{ApiKeyScopes.McpRead}' scope on your API key.";
    public const string WriteScopeError = $"Error: this tool requires the '{ApiKeyScopes.McpWrite}' scope on your API key.";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string Json(object value) => JsonSerializer.Serialize(value, JsonOptions);

    /// <summary>True when the current request's principal carries the given scope.</summary>
    public static bool HasScope<T>(RequestContext<T> context, string scope) =>
        context.User is not null && ApiKeyScopes.HasScope(context.User, scope);

    /// <summary>Resolves the owner for the current request's principal (null when unauthenticated).</summary>
    public static async Task<long?> ResolveOwnerAsync<T>(RequestContext<T> context, UserIdentityService identity) =>
        await identity.ResolveOwnerUserIdAsync(context.User ?? new ClaimsPrincipal());

    /// <summary>
    /// Resolves a link owned by <paramref name="ownerUserId"/> by short code. On an
    /// ambiguous multi-domain match, a default-domain link wins (mirrors
    /// <c>ApiV1Endpoints.ResolveOwnedLinkAsync</c>).
    /// </summary>
    public static async Task<ShortenedUrl?> ResolveOwnedLinkAsync(
        AppDbContext db, long ownerUserId, string shortCode, CancellationToken ct)
    {
        var matches = await db.ShortenedUrls
            .Include(l => l.Domain)
            .Where(l => l.OwnerUserId == ownerUserId && l.ShortCode == shortCode)
            .ToListAsync(ct);

        if (matches.Count <= 1)
            return matches.FirstOrDefault();

        return matches.FirstOrDefault(l => l.DomainId == null) ?? matches[0];
    }
}
