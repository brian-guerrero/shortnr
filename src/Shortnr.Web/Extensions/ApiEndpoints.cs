using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Web.Helpers;
using Shortnr.Web.Models;
using Shortnr.Web.Services;

namespace Shortnr.Web.Extensions;

public static class ApiEndpoints
{
    public static IEndpointRouteBuilder MapApiEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/qr/{shortCode}", async (string shortCode, HttpContext ctx, QrService qr, AppDbContext db) =>
        {
            var link = await ShortUrlHelper.ResolveAsync(db, ctx.Request.Host.Host, shortCode);
            if (link is null) return Results.NotFound();

            var shortUrl = ShortUrlHelper.Build(ctx.Request.Scheme, ctx.Request.Host.Host, link);
            var png = qr.GeneratePng(shortUrl);
            return Results.File(png, contentType: "image/png", fileDownloadName: $"qr-{shortCode}.png");
        });

        app.MapGet("/api/metrics", async (AppDbContext db, HttpContext ctx, UserIdentityService identity) =>
        {
            var ownerUserId = await identity.ResolveOwnerUserIdAsync(ctx.User);
            var workspaceCtx = await identity.ResolveActiveWorkspaceContextAsync(ctx.User);
            var workspaceId = workspaceCtx?.WorkspaceId;

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
            linkQuery = ApplyMetricsScoping(linkQuery, ownerUserId, workspaceId);

            var totalLinks = await linkQuery.CountAsync();
            var totalClicks = await linkQuery.SumAsync(l => (long?)l.ClickCount) ?? 0;
            var topLinks = await linkQuery
                .OrderByDescending(l => l.ClickCount)
                .Take(10)
                .Select(l => new { l.ShortCode, l.LongUrl, l.ClickCount })
                .ToListAsync();

            var clickQuery = db.ClickEvents.AsQueryable();
            clickQuery = ApplyClickMetricsScoping(clickQuery, ownerUserId, workspaceId);

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

                    await context.Response.WriteAsync("event: data-update\ndata: \n\n");
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

        app.MapGet("/.well-known/shortnr-verify.txt", async (AppDbContext db, HttpContext context) =>
        {
            var host = context.Request.Host.Host?.ToLowerInvariant();
            if (host is null) return Results.NotFound();

            var token = await db.Domains
                .Where(d => d.Hostname == host)
                .Select(d => d.VerificationToken)
                .FirstOrDefaultAsync();
            if (token is null) return Results.NotFound();

            return Results.Text(token, "text/plain");
        });

        app.MapGet("/{shortCode}", async (string shortCode, AppDbContext db,
            Channel<ClickRecord> clickChannel, HttpContext context, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("Shortnr.Api.Redirect");

            // Project to only the columns needed — scalar/anonymous projections are
            // never change-tracked, keeping the hottest endpoint allocation-light.
            var host = context.Request.Host.Host?.ToLowerInvariant();
            var domainId = host is not null
                ? await db.Domains
                    .Where(d => d.Hostname == host && d.IsVerified)
                    .Select(d => (long?)d.Id)
                    .FirstOrDefaultAsync()
                : null;

            var link = await db.ShortenedUrls
                .Where(l => l.DomainId == domainId && l.ShortCode == shortCode)
                .Select(l => new { l.Id, l.LongUrl })
                .FirstOrDefaultAsync();

            if (link is null)
            {
                logger.LogWarning("Redirect requested for unknown shortCode={ShortCode} host={Host}", shortCode, host);
                return Results.NotFound();
            }

            var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            var ip = !string.IsNullOrWhiteSpace(forwardedFor)
                ? forwardedFor.Split(',')[0].Trim()
                : context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            clickChannel.Writer.TryWrite(new ClickRecord
            {
                ShortenedUrlId = link.Id,
                IpAddress = ip,
                UserAgent = context.Request.Headers["User-Agent"].FirstOrDefault() ?? "",
                Referer = context.Request.Headers["Referer"].FirstOrDefault() ?? ""
            });

            logger.LogInformation("Redirect shortCode={ShortCode} host={Host} ip={Ip}", shortCode, host, ip);

            return Results.Redirect(link.LongUrl);
        }).RequireRateLimiting("redirect-ip");

        return app;
    }

    private static IQueryable<ShortenedUrl> ApplyMetricsScoping(IQueryable<ShortenedUrl> query, long? ownerUserId, long? workspaceId)
    {
        if (workspaceId is not null)
            return query.Where(l => l.WorkspaceId == workspaceId);
        if (ownerUserId is not null)
            return query.Where(l => l.OwnerUserId == ownerUserId);
        return query;
    }

    private static IQueryable<ClickEvent> ApplyClickMetricsScoping(IQueryable<ClickEvent> query, long? ownerUserId, long? workspaceId)
    {
        if (workspaceId is not null)
            return query.Where(e => e.ShortenedUrl.WorkspaceId == workspaceId);
        if (ownerUserId is not null)
            return query.Where(e => e.ShortenedUrl.OwnerUserId == ownerUserId);
        return query;
    }
}
