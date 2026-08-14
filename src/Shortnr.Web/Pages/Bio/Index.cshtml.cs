using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;

namespace Shortnr.Web.Pages.Bio;

/// <summary>
/// The public, no-auth bio page at /bio/{slug}. Renders the owner's visible links
/// as buttons that point at real short links (so clicks and QRs come for free).
/// Unknown slugs and pages with no visible links return 404.
/// </summary>
public class IndexModel : PageModel
{
    private readonly AppDbContext _db;

    public BioPage? BioPage { get; private set; }

    public IndexModel(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> OnGet(string slug)
    {
        var page = await _db.BioPages
            .Include(b => b.Links)
            .ThenInclude(l => l.ShortenedUrl)
            .ThenInclude(s => s!.Domain)
            .FirstOrDefaultAsync(b => b.Slug == slug);

        if (page is null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return Page();
        }

        var visible = page.Links
            .Where(l => l.IsVisible && l.ShortenedUrl is not null && !l.ShortenedUrl.IsArchived)
            .OrderBy(l => l.SortOrder)
            .ThenBy(l => l.Id)
            .ToList();

        if (visible.Count == 0)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return Page();
        }

        page.Links = visible;
        BioPage = page;
        return Page();
    }
}
