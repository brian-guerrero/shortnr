using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Web.Models;
using Shortnr.Web.Services;

namespace Shortnr.Web.Pages;

/// <summary>
/// AI activity dashboard: lists <c>AiActivityLog</c> rows describing actions
/// AI assistants performed through the MCP API on behalf of the current owner.
/// Mirrors the Dashboard page's access-control and owner-scoping conventions.
/// </summary>
public class ActivityModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly UserIdentityService _identity;

    public ActivityModel(AppDbContext db, UserIdentityService identity)
    {
        _db = db;
        _identity = identity;
    }

    public List<AiActivityRow> Activity { get; set; } = [];
    public ActiveWorkspaceContext? Workspace { get; set; }

    public async Task<IActionResult> OnGet()
    {
        if (_identity.IsAuthEnabled && User.Identity?.IsAuthenticated != true)
        {
            if (Request.Headers["HX-Request"].Count > 0)
                return Unauthorized();

            return RedirectToPage("/Index");
        }

        var ownerUserId = await _identity.ResolveOwnerUserIdAsync(User);
        Workspace = await _identity.ResolveActiveWorkspaceContextAsync(User);

        if (Request.Headers["HX-Request"].Count > 0)
        {
            return Partial("Shared/_AiActivity", await LoadActivityAsync(ownerUserId));
        }

        Activity = await LoadActivityAsync(ownerUserId);
        return Page();
    }

    private async Task<List<AiActivityRow>> LoadActivityAsync(long? ownerUserId)
    {
        var query = _db.AiActivityLogs.AsNoTracking().AsQueryable();
        if (ownerUserId is not null)
            query = query.Where(a => a.OwnerUserId == ownerUserId);

        return await query
            .OrderByDescending(a => a.CreatedAtUtc)
            .ThenByDescending(a => a.Id)
            .Take(50)
            .Select(a => new AiActivityRow
            {
                Id = a.Id,
                Action = a.Action,
                TargetEntityType = a.TargetEntityType,
                TargetEntityId = a.TargetEntityId,
                Summary = a.Summary,
                CreatedAtUtc = a.CreatedAtUtc,
                ApiKeyLabel = a.ApiKey != null ? a.ApiKey.Label : null
            })
            .ToListAsync();
    }
}
