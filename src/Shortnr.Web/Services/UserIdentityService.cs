using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;

namespace Shortnr.Web.Services;

/// <summary>
/// Resolves the current authenticated user's database identity.
/// Registered as a scoped service so it can hold per-request state.
/// </summary>
public class UserIdentityService(AppDbContext db, IConfiguration config, IHttpContextAccessor httpContextAccessor)
{
    public bool IsAuthEnabled => config.GetValue<bool>("Authentication:Enabled", defaultValue: true);

    /// <summary>
    /// Returns the <c>Users.Id</c> for the currently authenticated principal, or
    /// <c>null</c> when auth is disabled, the user is not authenticated, or the
    /// provisioning queue hasn't written the row yet (narrow first-login race).
    /// API-key principals carry the already-resolved owner id and short-circuit
    /// the issuer/subject lookup.
    /// </summary>
    public async Task<long?> ResolveOwnerUserIdAsync(ClaimsPrincipal principal)
    {
        if (!IsAuthEnabled) return null;
        if (principal.Identity?.IsAuthenticated != true) return null;

        var apiKeyOwner = principal.FindFirstValue(ApiKeyHandler.ApiKeyIdClaim);
        if (apiKeyOwner is not null && long.TryParse(apiKeyOwner, out var ownerId))
            return ownerId;

        var subject = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                   ?? principal.FindFirstValue("sub");
        if (subject is null) return null;

        var issuer = config["Authentication:Oidc:Authority"] ?? string.Empty;
        var user = await db.Users.FirstOrDefaultAsync(u => u.Issuer == issuer && u.Subject == subject);
        return user?.Id;
    }

    /// <summary>
    /// Reads the <c>snr_workspace</c> cookie to determine which workspace the user
    /// is currently operating in, validates their membership, and returns metadata.
    /// Returns null when no workspace is active or auth is disabled.
    /// </summary>
    public async Task<ActiveWorkspaceContext?> ResolveActiveWorkspaceContextAsync(ClaimsPrincipal principal)
    {
        if (!IsAuthEnabled) return null;

        var userId = await ResolveOwnerUserIdAsync(principal);
        if (userId is null) return null;

        var httpContext = httpContextAccessor.HttpContext;
        var cookie = httpContext?.Request.Cookies["snr_workspace"];
        if (string.IsNullOrWhiteSpace(cookie)) return null;

        var workspace = await db.Workspaces
            .FirstOrDefaultAsync(w => w.Slug == cookie);
        if (workspace is null) return null;

        var member = await db.WorkspaceMembers
            .FirstOrDefaultAsync(m => m.WorkspaceId == workspace.Id && m.UserId == userId.Value && m.JoinedAtUtc != null);
        if (member is null) return null;

        return new ActiveWorkspaceContext
        {
            WorkspaceId = workspace.Id,
            Slug = workspace.Slug,
            Name = workspace.Name,
            Role = member.Role
        };
    }
}

public class ActiveWorkspaceContext
{
    public required long WorkspaceId { get; init; }
    public required string Slug { get; init; } = string.Empty;
    public required string Name { get; init; } = string.Empty;
    public WorkspaceRole Role { get; init; }
}
