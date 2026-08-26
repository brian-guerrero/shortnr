using System.Security.Claims;
using Shortnr.Web.Features.Theming;

namespace Shortnr.Web.Features.Authentication;

public interface IUserIdentityService
{
    bool IsAuthEnabled { get; }

    Task<long?> ResolveOwnerUserIdAsync(ClaimsPrincipal principal);

    Task<ActiveWorkspaceContext?> ResolveActiveWorkspaceContextAsync(ClaimsPrincipal principal);

    /// <summary>
    /// Resolves the current principal's app-wide theme preference
    /// (<c>User.PreferredTheme</c>) through <see cref="IThemeResolver"/>, so
    /// an unknown/deleted community theme id falls back to
    /// <see cref="ThemeCatalog.Default"/> the same way every other theme
    /// consumer does. Returns <see cref="ThemeCatalog.Default"/> when auth is
    /// disabled, the principal isn't authenticated, or the provisioning queue
    /// hasn't written their <c>Users</c> row yet.
    /// </summary>
    Task<Theme> ResolveThemePreferenceAsync(ClaimsPrincipal principal);
}