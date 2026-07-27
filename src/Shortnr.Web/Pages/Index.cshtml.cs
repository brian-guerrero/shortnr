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

    public List<ShortenedUrl> RecentLinks { get; set; } = [];
    public bool IsHtmxRequest { get; set; }

    public IndexModel(AppDbContext db)
    {
        _db = db;
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

        var shortCode = GenerateShortCode();
        var shortened = new ShortenedUrl
        {
            LongUrl = url,
            ShortCode = shortCode,
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.ShortenedUrls.Add(shortened);
        await _db.SaveChangesAsync();

        var recentLinks = await _db.ShortenedUrls
            .OrderByDescending(l => l.CreatedAtUtc)
            .Take(10)
            .ToListAsync();

        var baseUrl = $"{Request.Scheme}://{Request.Host}/{shortCode}";
        var model = new PostResultViewModel { ShortUrl = baseUrl, RecentLinks = recentLinks };
        return Partial("Shared/_PostResult", model);
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
