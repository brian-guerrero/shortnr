using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;

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
        var rows = string.Concat(recentLinks.Select(l => $"""
                    <tr>
                        <td><a href="/{l.ShortCode}" target="_blank">/{l.ShortCode}</a></td>
                        <td style="max-width: 300px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap;">{l.LongUrl}</td>
                        <td>{l.ClickCount}</td>
                    </tr>
"""));
        var html = $"""
<ins style="display:block; margin-top: 1rem;">
    <strong>Short URL:</strong>
    <a href="{baseUrl}" target="_blank">{baseUrl}</a>
</ins>
<section id="recent" hx-swap-oob="true">
    <h2>Recent links</h2>
    <table>
        <thead>
            <tr><th>Short URL</th><th>Original</th><th>Clicks</th></tr>
        </thead>
        <tbody>
{rows}
        </tbody>
    </table>
</section>
""";
        return Content(html, "text/html");
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
