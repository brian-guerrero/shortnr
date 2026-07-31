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
    public string? DefaultHostname { get; set; }

    public IndexModel(AppDbContext db, UserIdentityService identity)
    {
        _db = db;
        _identity = identity;
    }

    public async Task OnGet()
    {
        IsHtmxRequest = Request.Headers["HX-Request"].Count > 0;
        var ownerUserId = await _identity.ResolveOwnerUserIdAsync(User);
        var defaultDomain = await ResolveDefaultDomainAsync(ownerUserId);
        DefaultHostname = defaultDomain?.Hostname;
        RecentLinks = await RecentLinksAsync(ownerUserId);
    }

    public async Task<IActionResult> OnPost()
    {
        var url = Request.Form["url"].FirstOrDefault() ?? "";
        var slug = Request.Form["slug"].FirstOrDefault()?.Trim() ?? "";

        var ownerUserId = await _identity.ResolveOwnerUserIdAsync(User);
        var defaultDomain = await ResolveDefaultDomainAsync(ownerUserId);
        var domainId = defaultDomain?.Id;

        if (string.IsNullOrWhiteSpace(url))
            return await ErrorResultAsync("Enter a URL to shorten.", ownerUserId);

        if (slug.Length > 0)
        {
            if (!ShortLinkCodes.IsValidSlug(slug))
                return await ErrorResultAsync("Custom code must be 1–64 characters: letters, digits, '-' and '_', starting with a letter or digit.", ownerUserId);

            var collides = await _db.ShortenedUrls.AnyAsync(l => l.DomainId == domainId && l.ShortCode == slug);
            if (collides)
                return await ErrorResultAsync($"The custom code '{slug}' is already taken.", ownerUserId);

            return await CreateAsync(url, slug, defaultDomain, ownerUserId);
        }

        var existing = await _db.ShortenedUrls.FirstOrDefaultAsync(l => l.DomainId == domainId && l.LongUrl == url);
        if (existing is not null)
        {
            var baseUrl = BuildShortUrl(defaultDomain, existing.ShortCode);
            var recentLinks = await RecentLinksAsync(ownerUserId);
            return Partial("Shared/_PostResult", new PostResultViewModel { ShortUrl = baseUrl, ShortCode = existing.ShortCode, RecentLinks = recentLinks });
        }

        return await CreateAsync(url, await ShortLinkCodes.GenerateUniqueCodeAsync(code =>
            _db.ShortenedUrls.AnyAsync(l => l.DomainId == domainId && l.ShortCode == code)), defaultDomain, ownerUserId);
    }

    private async Task<IActionResult> CreateAsync(string url, string shortCode, Domain? defaultDomain, long? ownerUserId)
    {
        var shortened = new ShortenedUrl
        {
            LongUrl = url,
            ShortCode = shortCode,
            DomainId = defaultDomain?.Id,
            CreatedAtUtc = DateTime.UtcNow,
            // Best-effort: provisioning is async so OwnerUserId may be null on first login.
            OwnerUserId = ownerUserId
        };
        _db.ShortenedUrls.Add(shortened);
        await _db.SaveChangesAsync();

        var recentLinks = await RecentLinksAsync(ownerUserId);
        var baseUrl = BuildShortUrl(defaultDomain, shortCode);
        return Partial("Shared/_PostResult", new PostResultViewModel { ShortUrl = baseUrl, ShortCode = shortCode, RecentLinks = recentLinks });
    }

    private async Task<IActionResult> ErrorResultAsync(string message, long? ownerUserId)
    {
        var defaultDomain = await ResolveDefaultDomainAsync(ownerUserId);
        var recentLinks = await RecentLinksAsync(ownerUserId);
        return Partial("Shared/_PostResult", new PostResultViewModel { HasError = true, ErrorMessage = message, RecentLinks = recentLinks });
    }

    /// <summary>
    /// Resolves the signed-in owner's verified default domain, if any. When auth is
    /// disabled no owner is resolved, so the instance default host is used.
    /// </summary>
    private async Task<Domain?> ResolveDefaultDomainAsync(long? ownerUserId)
    {
        if (ownerUserId is null)
            return null;

        return await _db.Domains.FirstOrDefaultAsync(d =>
            d.OwnerUserId == ownerUserId && d.IsVerified && d.IsDefault);
    }

    private string BuildShortUrl(Domain? defaultDomain, string shortCode) =>
        $"{Request.Scheme}://{(defaultDomain?.Hostname ?? Request.Host.Host)}/{shortCode}";

    /// <summary>
    /// Recent links shown on the home page. Scoped to the signed-in owner's links
    /// across every domain they own (or the instance host's links when anonymous),
    /// so switching the default domain never makes existing links disappear.
    /// </summary>
    private Task<List<ShortenedUrl>> RecentLinksAsync(long? ownerUserId) =>
        _db.ShortenedUrls
            .Include(l => l.Domain)
            .Where(l => ownerUserId == null ? l.DomainId == null : l.OwnerUserId == ownerUserId)
            .OrderByDescending(l => l.CreatedAtUtc)
            .Take(10)
            .ToListAsync();
}
