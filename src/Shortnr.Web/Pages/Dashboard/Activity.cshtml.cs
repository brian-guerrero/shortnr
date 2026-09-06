using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;

namespace Shortnr.Web.Pages;

/// <summary>
/// AI activity dashboard: lists <c>AiActivityLog</c> rows describing actions
/// AI assistants performed through the MCP API on behalf of the current owner.
/// Mirrors the Dashboard page's access-control and owner-scoping conventions.
/// </summary>
public class ActivityModel(AppDbContext db, UserIdentityService identity)
    : AuthorizedPageModel(identity)
{
    public List<AiActivityRow> Activity { get; set; } = [];

    public async Task<IActionResult> OnGet()
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        var ownerUserId = await ResolveOwnerUserIdAsync();

        if (IsHtmxRequest)
        {
            return Partial("Shared/_AiActivity", await LoadActivityAsync(ownerUserId));
        }

        Activity = await LoadActivityAsync(ownerUserId);
        return Page();
    }

    private async Task<List<AiActivityRow>> LoadActivityAsync(long? ownerUserId)
    {
        var query = db.AiActivityLogs.AsNoTracking().AsQueryable();
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
