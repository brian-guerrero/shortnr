using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Web.Models;
using Shortnr.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddSingleton(Channel.CreateUnbounded<ClickRecord>());
builder.Services.AddHostedService<ClickBatchProcessor>();
builder.Services.AddSingleton<QrService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.UseStaticFiles();
app.UseRouting();

app.MapRazorPages();

app.MapGet("/api/metrics", async (AppDbContext db) =>
{
    var totalLinks = await db.ShortenedUrls.CountAsync();
    var totalClicks = await db.ShortenedUrls.SumAsync(l => (long?)l.ClickCount) ?? 0;
    var topLinks = await db.ShortenedUrls
        .OrderByDescending(l => l.ClickCount)
        .Take(10)
        .Select(l => new { l.ShortCode, l.LongUrl, l.ClickCount })
        .ToListAsync();

    return Results.Json(new { totalLinks, totalClicks, topLinks });
});

app.MapGet("/{shortCode}", async (string shortCode, AppDbContext db, Channel<ClickRecord> clickChannel, HttpContext context) =>
{
    var link = await db.ShortenedUrls.FirstOrDefaultAsync(l => l.ShortCode == shortCode);
    if (link is null) return Results.NotFound();

    clickChannel.Writer.TryWrite(new ClickRecord
    {
        ShortCode = shortCode,
        IpAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        UserAgent = context.Request.Headers["User-Agent"].FirstOrDefault() ?? "",
        Referer = context.Request.Headers["Referer"].FirstOrDefault() ?? ""
    });

    return Results.Redirect(link.LongUrl);
});

app.Run();
