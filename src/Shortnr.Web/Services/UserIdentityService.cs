using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;

namespace Shortnr.Web.Services;

/// <summary>
/// Resolves the current authenticated user's database identity.
/// Registered as a scoped service so it can hold per-request state.
/// </summary>
public class UserIdentityService(AppDbContext db, IConfiguration config)
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
}
