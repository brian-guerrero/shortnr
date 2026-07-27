using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.UseHttpsRedirection();

app.MapGet("/", async (AppDbContext db) =>
{
    var links = await db.ShortenedUrls.OrderByDescending(l => l.CreatedAtUtc).Take(10).ToListAsync();
    var html = $"""
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>shortnr</title>
    <link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/@picocss/pico@2/css/pico.min.css">
    <script src="https://unpkg.com/htmx.org@2"></script>
</head>
<body>
    <main class="container" style="max-width: 640px; padding-top: 4rem;">
        <article>
            <header><h1>shortnr</h1></header>
            <form hx-post="/" hx-target="#result" hx-swap="innerHTML">
                <label for="url">Enter a long URL</label>
                <input type="url" name="url" id="url" placeholder="https://example.com/very/long/path" required>
                <button type="submit">Shorten</button>
            </form>
            <div id="result"></div>
        </article>

        <section id="recent">
            <h2>Recent links</h2>
            <table>
                <thead>
                    <tr><th>Short URL</th><th>Original</th><th>Clicks</th></tr>
                </thead>
                <tbody>
                    {string.Concat(links.Select(l => $"""
                    <tr>
                        <td><a href="/{l.ShortCode}" target="_blank">/{l.ShortCode}</a></td>
                        <td style="max-width: 300px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap;">{l.LongUrl}</td>
                        <td>{l.ClickCount}</td>
                    </tr>
                    """))}
                </tbody>
            </table>
        </section>
    </main>
</body>
</html>
""";
    return Results.Content(html, "text/html");
});

app.MapPost("/", async (string url, AppDbContext db, HttpRequest request) =>
{
    var shortCode = GenerateShortCode();
    var shortened = new ShortenedUrl
    {
        LongUrl = url,
        ShortCode = shortCode,
        CreatedAtUtc = DateTime.UtcNow
    };
    db.ShortenedUrls.Add(shortened);
    await db.SaveChangesAsync();

    var baseUrl = $"{request.Scheme}://{request.Host}";
    var html = $"""
<ins style="display:block; margin-top: 1rem;">
    <strong>Short URL:</strong>
    <a href="{baseUrl}/{shortCode}" target="_blank">{baseUrl}/{shortCode}</a>
</ins>
""";
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
