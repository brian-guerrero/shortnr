using System.Security.Claims;

namespace Shortnr.Web.Features.Authentication;

public interface IUserIdentityService
{
    bool IsAuthEnabled { get; }

    Task<long?> ResolveOwnerUserIdAsync(ClaimsPrincipal principal);

    Task<ActiveWorkspaceContext?> ResolveActiveWorkspaceContextAsync(ClaimsPrincipal principal);
}