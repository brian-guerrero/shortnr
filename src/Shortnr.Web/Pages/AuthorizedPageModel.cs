using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Shortnr.Web.Pages;

/// <summary>
/// Shared plumbing for the pages behind the sign-in gate: the htmx-aware access
/// check, the htmx request flag the page shells use to pick a layout, and the
/// status/error banner pair the list partials render.
/// </summary>
public abstract class AuthorizedPageModel(UserIdentityService identity) : PageModel, IStatusMessageModel
{
    protected UserIdentityService Identity => identity;

    public bool IsHtmxRequest => Request.Headers["HX-Request"].Count > 0;

    public string? StatusMessage { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Returns <c>null</c> when the request may proceed, otherwise the result to
    /// return from the handler. htmx requests get a 401 so the caller can react
    /// in place; full page loads bounce to the sign-in page.
    /// </summary>
    protected virtual IActionResult? EnforceAccess()
    {
        if (identity.IsAuthEnabled && User.Identity?.IsAuthenticated != true)
            return IsHtmxRequest ? Unauthorized() : RedirectToPage("/Index");

        return null;
    }

    protected Task<long?> ResolveOwnerUserIdAsync() => identity.ResolveOwnerUserIdAsync(User);

    protected Task<ActiveWorkspaceContext?> ResolveWorkspaceAsync() =>
        identity.ResolveActiveWorkspaceContextAsync(User);
}

/// <summary>Rendered by <c>Shared/_StatusBanner</c>.</summary>
public interface IStatusMessageModel
{
    string? StatusMessage { get; }
    string? ErrorMessage { get; }
}
