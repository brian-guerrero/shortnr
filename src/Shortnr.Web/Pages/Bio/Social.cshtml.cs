using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Shortnr.Web.Features.Authentication;
using Shortnr.Web.Features.Social;

namespace Shortnr.Web.Pages.Bio;

/// <summary>
/// Settings page at /bio/social showing linked social accounts and their token health status.
/// </summary>
public class SocialModel : PageModel, IStatusMessages
{
    private readonly UserIdentityService _identity;
    private readonly SocialAccountService _socialAccounts;
    private readonly SocialTokenRefreshOptions _refreshOptions;

    public IReadOnlyList<SocialAccountViewModel> Accounts { get; private set; } = [];
    public bool IsHtmxRequest { get; set; }
    public string? StatusMessage { get; set; }
    public string? ErrorMessage { get; set; }

    public SocialModel(
        UserIdentityService identity,
        SocialAccountService socialAccounts,
        Microsoft.Extensions.Options.IOptions<SocialTokenRefreshOptions> refreshOptions)
    {
        _identity = identity;
        _socialAccounts = socialAccounts;
        _refreshOptions = refreshOptions.Value;
    }

    public async Task<IActionResult> OnGet()
    {
        if (!await IsAuthenticated())
            return Request.Headers["HX-Request"].Count > 0
                ? Unauthorized()
                : RedirectToPage("/Index");

        IsHtmxRequest = Request.Headers["HX-Request"].Count > 0;
        await LoadAccountsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostRemove(long id)
    {
        if (!await IsAuthenticated())
            return Unauthorized();

        var ownerUserId = await _identity.ResolveOwnerUserIdAsync(User);
        if (ownerUserId is null)
        {
            ErrorMessage = "Could not resolve your account.";
            await LoadAccountsAsync();
            return Partial("Shared/_StatusMessages", this);
        }

        var deleted = await _socialAccounts.DeleteAsync(id, ownerUserId.Value);
        if (!deleted)
        {
            ErrorMessage = "Account not found.";
        }
        else
        {
            StatusMessage = "Social account unlinked.";
        }

        await LoadAccountsAsync();
        return Partial("Shared/_StatusMessages", this);
    }

    private async Task LoadAccountsAsync()
    {
        var ownerUserId = await _identity.ResolveOwnerUserIdAsync(User);
        if (ownerUserId is null)
        {
            Accounts = [];
            return;
        }

        var accounts = await _socialAccounts.GetByOwnerAsync(ownerUserId.Value);
        Accounts = accounts
            .Select(a => SocialAccountMapper.ToViewModel(a, _refreshOptions.RefreshWindowHours))
            .ToList();
    }

    private async Task<bool> IsAuthenticated()
    {
        if (_identity.IsAuthEnabled && User.Identity?.IsAuthenticated != true)
            return false;
        return true;
    }
}
