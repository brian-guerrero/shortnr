using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Web.Models;
using Shortnr.Web.Services;

namespace Shortnr.Web.Pages;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly UserIdentityService _identity;

    public List<ShortenedUrl> RecentLinks { get; set; } = [];
    public bool IsHtmxRequest { get; set; }

    public IndexModel(AppDbContext db, UserIdentityService identity)
    {
        _db = db;
        _identity = identity;
    }

    public async Task OnGet()
    {
        IsHtmxRequest = Request.Headers["HX-Request"].Count > 0;
        RecentLinks = await RecentDefaultDomainLinksAsync();
    }

    public async Task<IActionResult> OnPost()
    {
        var url = Request.Form["url"].FirstOrDefault() ?? "";
        var slug = Request.Form["slug"].FirstOrDefault()?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(url))
            return await ErrorResultAsync("Enter a URL to shorten.");

        if (slug.Length > 0)
        {
            if (!ShortLinkCodes.IsValidSlug(slug))
                return await ErrorResultAsync("Custom code must be 1–64 characters: letters, digits, '-' and '_', starting with a letter or digit.");

            var collides = await _db.ShortenedUrls.AnyAsync(l => l.DomainId == null && l.ShortCode == slug);
            if (collides)
                return await ErrorResultAsync($"The custom code '{slug}' is already taken.");

            return await CreateAsync(url, slug);
        }

        var existing = await _db.ShortenedUrls.FirstOrDefaultAsync(l => l.DomainId == null && l.LongUrl == url);
        if (existing is not null)
        {
            var baseUrl = $"{Request.Scheme}://{Request.Host}/{existing.ShortCode}";
            var recentLinks = await RecentDefaultDomainLinksAsync();
            return Partial("Shared/_PostResult", new PostResultViewModel { ShortUrl = baseUrl, ShortCode = existing.ShortCode, RecentLinks = recentLinks });
        }

        return await CreateAsync(url, await ShortLinkCodes.GenerateUniqueCodeAsync(code =>
            _db.ShortenedUrls.AnyAsync(l => l.DomainId == null && l.ShortCode == code)));
    }

    private async Task<IActionResult> CreateAsync(string url, string shortCode)
    {
        var shortened = new ShortenedUrl
        {
            LongUrl = url,
            ShortCode = shortCode,
            CreatedAtUtc = DateTime.UtcNow,
            // Best-effort: provisioning is async so OwnerUserId may be null on first login.
            OwnerUserId = await _identity.ResolveOwnerUserIdAsync(User)
        };
        _db.ShortenedUrls.Add(shortened);
        await _db.SaveChangesAsync();

        var recentLinks = await RecentDefaultDomainLinksAsync();
        var baseUrl = $"{Request.Scheme}://{Request.Host}/{shortCode}";
        return Partial("Shared/_PostResult", new PostResultViewModel { ShortUrl = baseUrl, ShortCode = shortCode, RecentLinks = recentLinks });
    }

    private async Task<IActionResult> ErrorResultAsync(string message)
    {
        var recentLinks = await RecentDefaultDomainLinksAsync();
        return Partial("Shared/_PostResult", new PostResultViewModel { HasError = true, ErrorMessage = message, RecentLinks = recentLinks });
    }

    private Task<List<ShortenedUrl>> RecentDefaultDomainLinksAsync() =>
        _db.ShortenedUrls
            .Where(l => l.DomainId == null)
            .OrderByDescending(l => l.CreatedAtUtc)
            .Take(10)
            .ToListAsync();
}
