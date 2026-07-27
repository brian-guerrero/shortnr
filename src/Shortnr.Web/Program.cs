using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddSingleton(Channel.CreateUnbounded<string>());
builder.Services.AddHostedService<ClickBatchProcessor>();

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

app.MapGet("/{shortCode}", async (string shortCode, AppDbContext db, Channel<string> clickChannel) =>
{
    var link = await db.ShortenedUrls.FirstOrDefaultAsync(l => l.ShortCode == shortCode);
    if (link is null) return Results.NotFound();

    clickChannel.Writer.TryWrite(shortCode);

    return Results.Redirect(link.LongUrl);
});

app.Run();
