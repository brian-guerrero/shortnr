using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;

namespace Shortnr.Web.Pages.Settings;

public class WebhooksModel : PageModel, IStatusMessages
{
    private readonly AppDbContext _db;
    private readonly UserIdentityService _identity;

    public List<Webhook> Webhooks { get; set; } = [];
    public string? StatusMessage { get; set; }
    public string? ErrorMessage { get; set; }
    public string? CreatedSecret { get; set; }
    public bool IsHtmxRequest { get; set; }

    public WebhooksModel(AppDbContext db, UserIdentityService identity)
    {
        _db = db;
        _identity = identity;
    }

    public async Task<IActionResult> OnGet()
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        IsHtmxRequest = Request.Headers["HX-Request"].Count > 0;
        Webhooks = await LoadWebhooksAsync();
        return Page();
    }

    public async Task<IActionResult> OnPost()
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        var url = (Request.Form["url"].FirstOrDefault() ?? "").Trim();
        if (url.Length == 0)
            return await ListPartialAsync(error: "Enter a webhook URL.");

        var (isValid, validationError) = WebhookUrlValidator.Validate(url);
        if (!isValid)
            return await ListPartialAsync(error: validationError ?? "Invalid webhook URL.");

        var selectedEvents = Request.Form["event"].ToArray();
        if (selectedEvents.Length == 0 || !selectedEvents.All(WebhookEventTypes.IsValid))
            return await ListPartialAsync(error: "Select at least one valid event type.");

        var ownerUserId = await _identity.ResolveOwnerUserIdAsync(User);
        if (ownerUserId is null)
            return await ListPartialAsync(error: "Unable to determine the webhook owner.");

        var secret = WebhookSigningService.GenerateSecret();
        _db.Webhooks.Add(new Webhook
        {
            OwnerUserId = ownerUserId.Value,
            Url = url,
            Secret = secret,
            EventTypes = WebhookEventTypes.Format(selectedEvents),
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        return await ListPartialAsync(
            status: "Webhook created. Copy the secret now — it is shown only once.",
            createdSecret: secret);
    }

    public async Task<IActionResult> OnPostRevoke(long id)
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        var webhook = await FindOwnedWebhookAsync(id);
        if (webhook is null)
            return await ListPartialAsync(error: "Webhook not found.");

        webhook.IsActive = false;
        await _db.SaveChangesAsync();

        return await ListPartialAsync(status: $"Webhook for '{webhook.Url}' disabled.");
    }

    public async Task<IActionResult> OnPostActivate(long id)
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        var webhook = await FindOwnedWebhookAsync(id);
        if (webhook is null)
            return await ListPartialAsync(error: "Webhook not found.");

        webhook.IsActive = true;
        webhook.FailureCount = 0;
        webhook.LastFailureAtUtc = null;
        await _db.SaveChangesAsync();

        return await ListPartialAsync(status: $"Webhook for '{webhook.Url}' re-enabled.");
    }

    private IActionResult? EnforceAccess()
    {
        if (_identity.IsAuthEnabled && User.Identity?.IsAuthenticated != true)
            return Request.Headers["HX-Request"].Count > 0
                ? Unauthorized()
                : RedirectToPage("/Index");

        return null;
    }

    private async Task<List<Webhook>> LoadWebhooksAsync()
    {
        var ownerUserId = await _identity.ResolveOwnerUserIdAsync(User);
        var query = _db.Webhooks.AsQueryable();
        if (ownerUserId is not null)
            query = query.Where(w => w.OwnerUserId == ownerUserId);

        return await query.OrderByDescending(w => w.CreatedAtUtc).ToListAsync();
    }

    private async Task<Webhook?> FindOwnedWebhookAsync(long id)
    {
        var ownerUserId = await _identity.ResolveOwnerUserIdAsync(User);
        return await _db.Webhooks.FirstOrDefaultAsync(w => w.Id == id && w.OwnerUserId == ownerUserId);
    }

    private async Task<IActionResult> ListPartialAsync(string? status = null, string? error = null, string? createdSecret = null)
    {
        StatusMessage = status;
        ErrorMessage = error;
        CreatedSecret = createdSecret;
        Webhooks = await LoadWebhooksAsync();
        return Partial("Shared/_WebhooksList", this);
    }
}
