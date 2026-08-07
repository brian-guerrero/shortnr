using System.Security.Claims;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Shortnr.Web.Features.Mcp.McpTools;

/// <summary>
/// Shared plumbing for MCP tools: scope checks against the authenticated
/// principal, owner resolution, owned-link lookup, destructive-action
/// confirmation, AI-activity audit logging, and JSON output formatting.
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

    /// <summary>The real <c>ApiKeys.Id</c> behind this request's key (null when not API-key auth).</summary>
    public static long? ResolveApiKeyId<T>(RequestContext<T> context) =>
        context.User is not null &&
        long.TryParse(context.User.FindFirst(ApiKeyHandler.ApiKeyIdValueClaim)?.Value, out var id)
            ? id
            : null;

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

    /// <summary>Enqueues an audit entry for an AI/MCP-initiated change (never blocks the call).</summary>
    public static void LogActivity(Channel<AiActivityRecord> channel, long ownerUserId, long? apiKeyId,
        string action, string? targetType, long? targetId, string summary) =>
        channel.Writer.TryWrite(new AiActivityRecord
        {
            OwnerUserId = ownerUserId,
            ApiKeyId = apiKeyId,
            Action = action,
            TargetEntityType = targetType,
            TargetEntityId = targetId,
            Summary = summary
        });

    public enum Confirmation { Approved, Declined, NeedsConfirmation }

    /// <summary>
    /// Resolves user confirmation for a destructive write using MRTR when the client
    /// supports it (throw <see cref="InputRequiredException"/> so the client prompts
    /// the user at the protocol level), the echoed <c>inputResponses</c> from an
    /// already-accepted MRTR flow, or an explicit <c>confirmed=true</c> argument as
    /// the safe down-level, session-less fallback (matching the PRD's compatibility table).
    /// </summary>
    public static Confirmation ResolveConfirmation(
        McpServer server, RequestContext<CallToolRequestParams> context,
        string inputKey, string message, string requestState, bool? confirmed)
    {
        if (confirmed == true)
            return Confirmation.Approved;

        if (context.Params?.InputResponses?.TryGetValue(inputKey, out var response) is true)
        {
            var elicited = response.Deserialize(InputResponse.ElicitResultJsonTypeInfo);
            return elicited?.IsAccepted is true ? Confirmation.Approved : Confirmation.Declined;
        }

        if (server.IsMrtrSupported)
            throw new InputRequiredException(
                inputRequests: new Dictionary<string, InputRequest>
                {
                    [inputKey] = InputRequest.ForElicitation(new() { Message = message })
                },
                requestState: requestState);

        return Confirmation.NeedsConfirmation;
    }
}
