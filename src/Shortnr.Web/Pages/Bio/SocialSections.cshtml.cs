using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Web.Features.Social;

namespace Shortnr.Web.Pages.Bio;

/// <summary>
/// HTMX endpoint that serves the dynamic social sections for a public bio page (PRD-021).
/// Returns empty when no social accounts are linked. Loads asynchronously so static
/// bio content renders first, then HTMX fetches this partial for progressive enhancement.
/// </summary>
public class SocialSectionsModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly ISocialCache _cache;

    public IReadOnlyList<SocialAccount> LinkedAccounts { get; private set; } = [];
    public Dictionary<long, SocialData?> SocialData { get; private set; } = new();

    public SocialSectionsModel(AppDbContext db, ISocialCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<IActionResult> OnGet(string slug)
    {
        // Find the bio page owner
        var bioPage = await _db.BioPages
            .AsNoTracking()
            .Where(b => b.Slug == slug)
            .Select(b => new { b.OwnerUserId })
            .FirstOrDefaultAsync();

        if (bioPage is null)
        {
            LinkedAccounts = [];
            return Page();
        }

        // Load linked social accounts (personal scope only — bio pages are owner-scoped)
        LinkedAccounts = await _db.SocialAccounts
            .AsNoTracking()
            .Where(a => a.IsLinked && a.OwnerUserId == bioPage.OwnerUserId)
            .OrderBy(a => a.Provider)
            .ToListAsync();

        // Load social data from cache (falls back to DB)
        foreach (var account in LinkedAccounts)
        {
            var data = await _cache.GetAsync(account.Id);
            SocialData[account.Id] = data;
        }

        return Page();
    }
}
