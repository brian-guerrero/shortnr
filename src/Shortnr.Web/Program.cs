using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Web.Models;
using Shortnr.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddRazorPages();
builder.Services.AddControllersWithViews();
builder.Services.AddScoped<ViewRenderService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.UseHttpsRedirection();

app.MapGet("/", async (ViewRenderService renderer, AppDbContext db, HttpRequest request) =>
{
    var links = await db.ShortenedUrls.OrderByDescending(l => l.CreatedAtUtc).Take(10).ToListAsync();
    var model = new DashboardViewModel { RecentLinks = links };
    var isHtmx = request.Headers["HX-Request"].Count > 0;
    var html = await renderer.RenderViewAsync("Index", model, isHtmx);
    return Results.Content(html, "text/html");
});

app.MapPost("/", async (HttpRequest request, AppDbContext db, ViewRenderService renderer) =>
{
    var url = request.Form["url"].FirstOrDefault() ?? "";

    var shortCode = GenerateShortCode();
    var shortened = new ShortenedUrl
    {
        LongUrl = url,
        ShortCode = shortCode,
        CreatedAtUtc = DateTime.UtcNow
    };
    db.ShortenedUrls.Add(shortened);
    await db.SaveChangesAsync();

    var baseUrl = $"{request.Scheme}://{request.Host}/{shortCode}";
    var html = await renderer.RenderViewAsync("Shared/_ShortResult", baseUrl, isPartial: true);
    return Results.Content(html, "text/html");
});

app.MapGet("/{shortCode}", async (string shortCode, AppDbContext db) =>
{
    var link = await db.ShortenedUrls.FirstOrDefaultAsync(l => l.ShortCode == shortCode);
    if (link is null) return Results.NotFound();

    link.ClickCount++;
    await db.SaveChangesAsync();

    return Results.Redirect(link.LongUrl);
});

app.Run();

static string GenerateShortCode()
{
    const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
    return string.Create(6, chars, (span, c) =>
    {
        var random = Random.Shared;
        for (var i = 0; i < span.Length; i++)
            span[i] = c[random.Next(c.Length)];
    });
}
