using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Web.Features.Authentication;
using Shortnr.Web.Features.Social;

namespace Shortnr.Web.Pages.Bio;

/// <summary>
/// Social account management page for bio pages (PRD-021).
/// Allows creators to link/unlink Twitter, Instagram, TikTok, and YouTube accounts.
/// </summary>
public class SocialModel : PageModel, IStatusMessages
{
    private readonly SocialAccountService _socialService;
    private readonly IUserIdentityService _identity;
    private readonly AppDbContext _db;

    public string? StatusMessage { get; set; }
    public string? ErrorMessage { get; set; }
    public bool IsAuthEnabled => _identity.IsAuthEnabled;
    public bool IsHtmxRequest => Request.Headers["HX-Request"].Count > 0;

    public IReadOnlyList<SocialProvider> Providers { get; } =
        [SocialProvider.Twitter, SocialProvider.Instagram, SocialProvider.YouTube, SocialProvider.TikTok];

    public IReadOnlyList<SocialAccount> LinkedAccounts { get; private set; } = [];

    public SocialModel(SocialAccountService socialService, IUserIdentityService identity, AppDbContext db)
    {
        _socialService = socialService;
        _identity = identity;
        _db = db;
    }

    public async Task<IActionResult> OnGet()
    {
        if (!IsAuthEnabled)
        {
            if (IsHtmxRequest) return Partial("Shared/_StatusMessages", this);
            return Page();
        }

        var userId = await _identity.ResolveOwnerUserIdAsync(HttpContext.User);
        if (userId is null)
        {
            if (IsHtmxRequest) return Partial("Shared/_StatusMessages", this);
            return Page();
        }

        LinkedAccounts = await _socialService.GetLinkedAccountsAsync(HttpContext.User);

        if (IsHtmxRequest) return Partial("Shared/_StatusMessages", this);
        return Page();
    }

    public async Task<IActionResult> OnGetConnect(string provider)
    {
        if (!IsAuthEnabled) return Unauthorized();

        var socialProvider = Enum.TryParse<SocialProvider>(provider, out var p) ? p : (SocialProvider?)null;
        if (socialProvider is null) return BadRequest("Unknown provider.");

        var providerService = _socialService.GetProvider(socialProvider.Value);
        if (providerService is null) return BadRequest("Provider not configured.");

        var redirectUri = Url.Page("/Bio/Social", "Callback", new { provider = socialProvider.Value }, Request.Scheme)!;
        var state = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{socialProvider.Value}:{Guid.NewGuid():N}"));

        HttpContext.Session.SetString("social_oauth_state", state);
        HttpContext.Session.SetString("social_oauth_provider", socialProvider.Value.ToString());

        var authUrl = providerService.BuildAuthorizationUrl(redirectUri, state);
        return Redirect(authUrl);
    }

    public async Task<IActionResult> OnGetCallback(string code, string state, string provider)
    {
        if (!IsAuthEnabled) return Unauthorized();

        var savedState = HttpContext.Session.GetString("social_oauth_state");
        var savedProvider = HttpContext.Session.GetString("social_oauth_provider");

        if (savedState is null || savedState != state || savedProvider != provider)
        {
            ErrorMessage = "Invalid OAuth state. Please try again.";
            return RedirectToPage();
        }

        HttpContext.Session.Remove("social_oauth_state");
        HttpContext.Session.Remove("social_oauth_provider");

        var socialProvider = Enum.TryParse<SocialProvider>(provider, out var p) ? p : (SocialProvider?)null;
        if (socialProvider is null)
        {
            ErrorMessage = "Unknown provider.";
            return RedirectToPage();
        }

        var providerService = _socialService.GetProvider(socialProvider.Value);
        if (providerService is null)
        {
            ErrorMessage = "Provider not configured.";
            return RedirectToPage();
        }

        var redirectUri = Url.Page("/Bio/Social", "Callback", new { provider = socialProvider.Value }, Request.Scheme)!;
        var tokens = await providerService.ExchangeCodeAsync(code, redirectUri);

        if (tokens is null)
        {
            ErrorMessage = $"Failed to authenticate with {socialProvider.Value}. Please try again.";
            return RedirectToPage();
        }

        var linked = await _socialService.LinkAccountAsync(HttpContext.User, socialProvider.Value, tokens);
        if (!linked)
        {
            ErrorMessage = "Failed to link account. Please try again.";
            return RedirectToPage();
        }

        StatusMessage = $"Successfully linked your {socialProvider.Value} account.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRefresh(string provider)
    {
        if (!IsAuthEnabled) return Unauthorized();

        var socialProvider = Enum.TryParse<SocialProvider>(provider, out var p) ? p : (SocialProvider?)null;
        if (socialProvider is null) return BadRequest("Unknown provider.");

        var userId = await _identity.ResolveOwnerUserIdAsync(HttpContext.User);
        if (userId is null) return Unauthorized();

        var account = await _socialService.GetAccountAsync(socialProvider.Value, userId.Value);
        if (account is null)
        {
            ErrorMessage = "Account not found.";
            LinkedAccounts = await _socialService.GetLinkedAccountsAsync(HttpContext.User);
            return Partial("Shared/_StatusMessages", this);
        }

        _socialService.TriggerRefresh(account.Id);
        StatusMessage = $"Refresh triggered for {socialProvider.Value}. Data will update shortly.";
        LinkedAccounts = await _socialService.GetLinkedAccountsAsync(HttpContext.User);
        return Partial("Shared/_StatusMessages", this);
    }

    public async Task<IActionResult> OnPostUnlink(string provider)
    {
        if (!IsAuthEnabled) return Unauthorized();

        var socialProvider = Enum.TryParse<SocialProvider>(provider, out var p) ? p : (SocialProvider?)null;
        if (socialProvider is null) return BadRequest("Unknown provider.");

        var unlinked = await _socialService.UnlinkAccountAsync(HttpContext.User, socialProvider.Value);
        if (!unlinked)
        {
            ErrorMessage = "Failed to unlink account.";
        }
        else
        {
            StatusMessage = $"Unlinked your {socialProvider.Value} account.";
        }

        LinkedAccounts = await _socialService.GetLinkedAccountsAsync(HttpContext.User);
        return Partial("Shared/_StatusMessages", this);
    }
}
