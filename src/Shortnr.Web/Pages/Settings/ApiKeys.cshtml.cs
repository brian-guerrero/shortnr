using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Web.Services;

namespace Shortnr.Web.Pages.Settings;

public class ApiKeysModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly UserIdentityService _identity;

    public List<ApiKey> ApiKeys { get; set; } = [];
    public string? StatusMessage { get; set; }
    public string? ErrorMessage { get; set; }
    public string? CreatedKey { get; set; }
    public bool IsHtmxRequest { get; set; }

    public ApiKeysModel(AppDbContext db, UserIdentityService identity)
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
        ApiKeys = await LoadApiKeysAsync();
        return Page();
    }

    public async Task<IActionResult> OnPost()
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        var label = (Request.Form["label"].FirstOrDefault() ?? "").Trim();
        if (label.Length == 0)
            return await ListPartialAsync(error: "Enter a label so you can recognise this key.");

        var ownerUserId = await _identity.ResolveOwnerUserIdAsync(User);
        if (ownerUserId is null)
            return await ListPartialAsync(error: "Unable to determine the key owner.");

        var key = ApiKeyService.GenerateKey();
        _db.ApiKeys.Add(new ApiKey
        {
            OwnerUserId = ownerUserId.Value,
            KeyHash = ApiKeyService.HashKey(key),
            KeyPrefix = key[..ApiKeyService.KeyPrefix.Length],
            Label = label,
            CreatedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        return await ListPartialAsync(
            status: "Key created. Copy it now — it is shown only once.",
            createdKey: key);
    }

    public async Task<IActionResult> OnPostRevoke(long id)
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        var key = await FindOwnedApiKeyAsync(id);
        if (key is null)
            return await ListPartialAsync(error: "API key not found.");

        key.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return await ListPartialAsync(status: $"API key '{key.Label}' revoked. Requests using it now fail authentication.");
    }

    private IActionResult? EnforceAccess()
    {
        if (_identity.IsAuthEnabled && User.Identity?.IsAuthenticated != true)
            return Request.Headers["HX-Request"].Count > 0
                ? Unauthorized()
                : RedirectToPage("/Index");

        return null;
    }

    private async Task<List<ApiKey>> LoadApiKeysAsync()
    {
        var ownerUserId = await _identity.ResolveOwnerUserIdAsync(User);
        var query = _db.ApiKeys.AsQueryable();
        if (ownerUserId is not null)
            query = query.Where(k => k.OwnerUserId == ownerUserId);

        return await query.OrderByDescending(k => k.CreatedAtUtc).ToListAsync();
    }

    private async Task<ApiKey?> FindOwnedApiKeyAsync(long id)
    {
        var ownerUserId = await _identity.ResolveOwnerUserIdAsync(User);
        return await _db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id && k.OwnerUserId == ownerUserId);
    }

    private async Task<IActionResult> ListPartialAsync(string? status = null, string? error = null, string? createdKey = null)
    {
        StatusMessage = status;
        ErrorMessage = error;
        CreatedKey = createdKey;
        ApiKeys = await LoadApiKeysAsync();
        return Partial("Shared/_ApiKeysList", this);
    }
}
