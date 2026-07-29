using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Web.Models;

namespace Shortnr.Web.Pages;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    public List<ShortenedUrl> RecentLinks { get; set; } = [];
    public bool IsHtmxRequest { get; set; }

    public IndexModel(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    public async Task OnGet()
    {
        IsHtmxRequest = Request.Headers["HX-Request"].Count > 0;
        RecentLinks = await _db.ShortenedUrls
            .OrderByDescending(l => l.CreatedAtUtc)
            .Take(10)
            .ToListAsync();
    }

    public async Task<IActionResult> OnPost()
    {
        var url = Request.Form["url"].FirstOrDefault() ?? "";

        var existing = await _db.ShortenedUrls.FirstOrDefaultAsync(l => l.LongUrl == url);
        if (existing is not null)
        {
            var baseUrl = $"{Request.Scheme}://{Request.Host}/{existing.ShortCode}";
            var recentLinks = await _db.ShortenedUrls
                .OrderByDescending(l => l.CreatedAtUtc)
                .Take(10)
                .ToListAsync();
            return Partial("Shared/_PostResult", new PostResultViewModel { ShortUrl = baseUrl, ShortCode = existing.ShortCode, RecentLinks = recentLinks });
        }

        var shortCode = GenerateShortCode();
        var shortened = new ShortenedUrl
        {
            LongUrl = url,
            ShortCode = shortCode,
            CreatedAtUtc = DateTime.UtcNow,
            OwnerUserId = await ResolveOwnerUserIdAsync()
        };
        _db.ShortenedUrls.Add(shortened);
        await _db.SaveChangesAsync();

        var recentLinks2 = await _db.ShortenedUrls
            .OrderByDescending(l => l.CreatedAtUtc)
            .Take(10)
            .ToListAsync();

        var baseUrl2 = $"{Request.Scheme}://{Request.Host}/{shortCode}";
        return Partial("Shared/_PostResult", new PostResultViewModel { ShortUrl = baseUrl2, ShortCode = shortCode, RecentLinks = recentLinks2 });
    }

    // Best-effort: the Users row for a brand-new signup is written asynchronously by
    // UserProvisioningProcessor (see Program.cs), so it may not exist yet if this is the
    // user's very first action right after login. Ownership is simply left unset in that
    // narrow race rather than duplicating the upsert here on the request path.
    private async Task<long?> ResolveOwnerUserIdAsync()
    {
        if (User.Identity?.IsAuthenticated != true) return null;

        var subject = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (subject is null) return null;

        var issuer = _config["Authentication:Oidc:Authority"] ?? string.Empty;
        var owner = await _db.Users.FirstOrDefaultAsync(u => u.Issuer == issuer && u.Subject == subject);
        return owner?.Id;
    }

    private static string GenerateShortCode()
    {
        const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        return string.Create(6, chars, (span, c) =>
        {
            var random = Random.Shared;
            for (var i = 0; i < span.Length; i++)
                span[i] = c[random.Next(c.Length)];
        });
    }
}
