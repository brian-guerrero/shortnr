using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Web.Models;
using Shortnr.Web.Services;

namespace Shortnr.Web.Extensions;

public static class ApiEndpoints
{
    public static IEndpointRouteBuilder MapApiEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/qr/{shortCode}", (string shortCode, HttpContext ctx, QrService qr) =>
        {
            var shortUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}/{shortCode}";
            var png = qr.GeneratePng(shortUrl);
            return Results.File(png, contentType: "image/png", fileDownloadName: $"qr-{shortCode}.png");
        });

        app.MapGet("/api/metrics", async (AppDbContext db, HttpContext ctx, UserIdentityService identity) =>
        {
            var ownerUserId = await identity.ResolveOwnerUserIdAsync(ctx.User);

            if (identity.IsAuthEnabled && ownerUserId is null)
                return Results.Json(new
                {
                    totalLinks = 0,
                    totalClicks = 0L,
                    totalCountries = 0,
                    topLinks = Array.Empty<object>(),
                    countryBreakdown = Array.Empty<object>(),
                    deviceBreakdown = Array.Empty<object>(),
                    browserBreakdown = Array.Empty<object>(),
                    osBreakdown = Array.Empty<object>()
                });

            var linkQuery = db.ShortenedUrls.AsQueryable();
            if (ownerUserId is not null)
                linkQuery = linkQuery.Where(l => l.OwnerUserId == ownerUserId);

            var totalLinks = await linkQuery.CountAsync();
            var totalClicks = await linkQuery.SumAsync(l => (long?)l.ClickCount) ?? 0;
            var topLinks = await linkQuery
                .OrderByDescending(l => l.ClickCount)
                .Take(10)
                .Select(l => new { l.ShortCode, l.LongUrl, l.ClickCount })
                .ToListAsync();

            var clickQuery = db.ClickEvents.AsQueryable();
            if (ownerUserId is not null)
                clickQuery = clickQuery.Where(e => e.ShortenedUrl.OwnerUserId == ownerUserId);

            var totalCountries = await clickQuery
                .Where(e => e.CountryCode != null && e.CountryCode != "")
                .Select(e => e.CountryCode)
                .Distinct()
                .CountAsync();

            var countryBreakdown = await clickQuery
                .Where(e => e.CountryCode != null && e.CountryCode != "")
                .GroupBy(e => new { e.CountryCode, e.CountryName })
                .Select(g => new { countryCode = g.Key.CountryCode, countryName = g.Key.CountryName, count = g.Count() })
                .OrderByDescending(x => x.count)
                .Take(10)
                .ToListAsync();

            var deviceBreakdown = await clickQuery
                .Where(e => e.DeviceFamily != null && e.DeviceFamily != "")
                .GroupBy(e => e.DeviceFamily)
                .Select(g => new { label = g.Key, count = g.Count() })
                .OrderByDescending(x => x.count)
                .Take(10)
                .ToListAsync();

            var browserBreakdown = await clickQuery
                .Where(e => e.Browser != null && e.Browser != "")
                .GroupBy(e => e.Browser)
                .Select(g => new { browser = g.Key, count = g.Count() })
                .OrderByDescending(x => x.count)
                .Take(10)
                .ToListAsync();

            var osBreakdown = await clickQuery
                .Where(e => e.OperatingSystem != null && e.OperatingSystem != "")
                .GroupBy(e => e.OperatingSystem)
                .Select(g => new { os = g.Key, count = g.Count() })
                .OrderByDescending(x => x.count)
                .Take(10)
                .ToListAsync();

            return Results.Json(new
            {
                totalLinks,
                totalClicks,
                totalCountries,
                topLinks,
                countryBreakdown,
                deviceBreakdown,
                browserBreakdown,
                osBreakdown
            });
        });

        app.MapGet("/api/events", async (HttpContext context, Channel<object> sseChannel, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("Shortnr.Api.SSE");
            logger.LogInformation("SSE connection established");

            context.Response.ContentType = "text/event-stream";
            context.Response.Headers["Cache-Control"] = "no-cache";
            context.Response.Headers["Connection"] = "keep-alive";
            context.Response.Headers["X-Accel-Buffering"] = "no";

            var ct = context.RequestAborted;
            try
            {
                while (await sseChannel.Reader.WaitToReadAsync(ct))
                {
                    while (sseChannel.Reader.TryRead(out _)) { }
                    logger.LogDebug("Data changed — sending update events");

                    await context.Response.WriteAsync("event: metrics-update\ndata: \n\n");
                    await context.Response.WriteAsync("event: geo-update\ndata: \n\n");
                    await context.Response.Body.FlushAsync();
                }
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("SSE connection closed by client");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "SSE connection terminated with error");
            }
        });

        app.MapGet("/{shortCode}", async (string shortCode, AppDbContext db,
            Channel<ClickRecord> clickChannel, HttpContext context, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("Shortnr.Api.Redirect");
            var link = await db.ShortenedUrls.FirstOrDefaultAsync(l => l.ShortCode == shortCode);
            if (link is null)
            {
                logger.LogWarning("Redirect requested for unknown shortCode={ShortCode}", shortCode);
                return Results.NotFound();
            }

            var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            var ip = !string.IsNullOrWhiteSpace(forwardedFor)
                ? forwardedFor.Split(',')[0].Trim()
                : context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            clickChannel.Writer.TryWrite(new ClickRecord
            {
                ShortCode = shortCode,
                IpAddress = ip,
                UserAgent = context.Request.Headers["User-Agent"].FirstOrDefault() ?? "",
                Referer = context.Request.Headers["Referer"].FirstOrDefault() ?? ""
            });

            logger.LogInformation("Redirect shortCode={ShortCode} ip={Ip}", shortCode, ip);

            return Results.Redirect(link.LongUrl);
        });

        return app;
    }
}
