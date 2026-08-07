using System.Security.Claims;

namespace Shortnr.Web.Features.Authentication;

/// <summary>
/// Canonical API-key scopes and helpers. A scope is granted to a key at creation
/// time and surfaced as <see cref="ScopeClaim"/> claims on the authenticated
/// principal by <see cref="ApiKeyHandler"/>, so authorization is claim-based.
/// Keys created before scopes existed store an empty <c>Scopes</c> value, which
/// is treated as full access (all scopes) for backward compatibility.
/// </summary>
public static class ApiKeyScopes
{
    public const string LinksRead = "links:read";
    public const string LinksWrite = "links:write";
    public const string McpRead = "mcp:read";
    public const string McpWrite = "mcp:write";

    /// <summary>All known scopes. An empty stored value grants these.</summary>
    public static readonly string[] All = [LinksRead, LinksWrite, McpRead, McpWrite];

    /// <summary>Scopes pre-selected when creating a key from the settings page.</summary>
    public static readonly string[] UiDefaults = [LinksRead, LinksWrite];

    /// <summary>Claim type that carries one granted scope per claim.</summary>
    public const string ScopeClaim = "snr_scope";

    /// <summary>
    /// Splits a stored space-separated scope string into its scopes. An empty or
    /// unknown input resolves to <see cref="All"/> (full access).
    /// </summary>
    public static IReadOnlyList<string> Resolve(string? storedScopes)
    {
        if (string.IsNullOrWhiteSpace(storedScopes))
            return All;

        return storedScopes
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(IsValid)
            .Distinct()
            .ToArray();
    }

    /// <summary>True when <paramref name="scope"/> is one of <see cref="All"/>.</summary>
    public static bool IsValid(string? scope) =>
        scope is not null && All.Contains(scope);

    /// <summary>
    /// True when the given selection of scopes is non-empty and every member is known.
    /// </summary>
    public static bool IsValidSelection(IEnumerable<string> scopes)
    {
        var list = scopes.ToList();
        return list.Count > 0 && list.All(IsValid);
    }

    /// <summary>Stable space-separated form for persisting a scope selection.</summary>
    public static string Format(IEnumerable<string> scopes) =>
        string.Join(' ', scopes.Where(IsValid).Distinct().Order());

    /// <summary>True when the principal carries the given scope claim.</summary>
    public static bool HasScope(ClaimsPrincipal principal, string scope) =>
        principal.HasClaim(ScopeClaim, scope);
}
