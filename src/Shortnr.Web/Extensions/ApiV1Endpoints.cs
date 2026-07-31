using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Web.Models;
using Shortnr.Web.Services;

namespace Shortnr.Web.Extensions;

/// <summary>
/// Versioned REST surface for creating and managing short links with API keys.
/// Every endpoint requires the "ApiKey" policy and is rate-limited per key.
/// </summary>
public static class ApiV1Endpoints
{
    public static void MapApiV1Endpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1")
            .RequireAuthorization(ApiKeyHandler.SchemeName)
            .RequireRateLimiting("api-key");

        group.MapPost("/links", CreateLinkAsync)
            .WithName("CreateLink")
            .WithSummary("Create a short link")
            .WithDescription("Accepts a long URL plus optional custom slug and verified custom domain.")
            .Produces<LinkResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status409Conflict);

        group.MapGet("/links", ListLinksAsync)
            .WithName("ListLinks")
            .WithSummary("List short links")
            .WithDescription("Paginated list scoped to the authenticated key's owner. Filters: domain (hostname or 'default'), from, to.")
            .Produces<LinkListResponse>();

        group.MapGet("/links/{shortCode}", GetLinkAsync)
            .WithName("GetLink")
            .WithSummary("Get a short link by code")
            .Produces<LinkResponse>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapPut("/links/{shortCode}", UpdateLinkAsync)
            .WithName("UpdateLink")
            .WithSummary("Update a short link")
            .WithDescription("Omitted fields keep their current value. Changing the slug or domain is subject to the same uniqueness rules as creation.")
            .Produces<LinkResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        group.MapDelete("/links/{shortCode}", DeleteLinkAsync)
            .WithName("DeleteLink")
            .WithSummary("Delete a short link")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/links/{shortCode}/clicks", GetLinkClicksAsync)
            .WithName("GetLinkClicks")
            .WithSummary("List click events for a short link")
            .WithDescription("Paginated click events, newest first.")
            .Produces<ClickListResponse>()
            .Produces(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> CreateLinkAsync(
        CreateLinkRequest body,
        HttpRequest request,
        AppDbContext db,
        UserIdentityService identity,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var ownerUserId = await identity.ResolveOwnerUserIdAsync(user);
        if (ownerUserId is null)
            return TypedResults.Unauthorized();

        var url = body.Url?.Trim() ?? "";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["url"] = ["Must be an absolute http(s) URL."] });

        Domain? domain = null;
        var domainId = (long?)null;
        if (!string.IsNullOrWhiteSpace(body.Domain))
        {
            var hostname = body.Domain.Trim().ToLowerInvariant();
            domain = await db.Domains.FirstOrDefaultAsync(
                d => d.Hostname == hostname && d.OwnerUserId == ownerUserId && d.IsVerified, ct);
            if (domain is null)
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["domain"] = ["Unknown, unowned or unverified domain."] });
            domainId = domain.Id;
        }
        else
        {
            // Fall back to the owner's verified default domain, mirroring the web UI.
            domain = await db.Domains.FirstOrDefaultAsync(
                d => d.OwnerUserId == ownerUserId && d.IsVerified && d.IsDefault, ct);
            domainId = domain?.Id;
        }

        var slug = body.Slug?.Trim() ?? "";
        string shortCode;
        if (slug.Length > 0)
        {
            if (!ShortLinkCodes.IsValidSlug(slug))
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["slug"] = ["Must be 1–64 chars: letters, digits, '-' or '_', starting with a letter or digit."] });

            var collides = await db.ShortenedUrls.AnyAsync(l => l.DomainId == domainId && l.ShortCode == slug, ct);
            if (collides)
                return TypedResults.Conflict();
            shortCode = slug;
        }
        else
        {
            shortCode = await ShortLinkCodes.GenerateUniqueCodeAsync(
                code => db.ShortenedUrls.AnyAsync(l => l.DomainId == domainId && l.ShortCode == code, ct));
        }

        var link = new ShortenedUrl
        {
            LongUrl = url,
            ShortCode = shortCode,
            DomainId = domainId,
            OwnerUserId = ownerUserId,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.ShortenedUrls.Add(link);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created(
            $"/api/v1/links/{shortCode}",
            ToResponse(link, domain, request.Scheme, request.Host.Host));
    }

    private static async Task<IResult> ListLinksAsync(
        int? page,
        int? pageSize,
        string? domain,
        HttpRequest request,
        AppDbContext db,
        UserIdentityService identity,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var ownerUserId = await identity.ResolveOwnerUserIdAsync(user);
        if (ownerUserId is null)
            return TypedResults.Unauthorized();

        var p = Math.Max(1, page ?? 1);
        var ps = Math.Clamp(pageSize ?? 20, 1, 100);

        var query = db.ShortenedUrls.Where(l => l.OwnerUserId == ownerUserId);
        if (!string.IsNullOrEmpty(domain))
        {
            query = domain == "default"
                ? query.Where(l => l.DomainId == null)
                : query.Where(l => l.Domain != null && l.Domain.Hostname == domain);
        }

        var total = await query.CountAsync(ct);
        var links = await query
            .OrderByDescending(l => l.CreatedAtUtc)
            .Include(l => l.Domain)
            .Skip((p - 1) * ps)
            .Take(ps)
            .ToListAsync(ct);

        return TypedResults.Ok(new LinkListResponse(
            links.Select(l => ToResponse(l, l.Domain, request.Scheme, request.Host.Host)).ToList(),
            p, ps, total));
    }

    private static async Task<IResult> GetLinkAsync(
        string shortCode,
        string? domain,
        HttpRequest request,
        AppDbContext db,
        UserIdentityService identity,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var ownerUserId = await identity.ResolveOwnerUserIdAsync(user);
        if (ownerUserId is null)
            return TypedResults.Unauthorized();

        var link = await ResolveOwnedLinkAsync(db, ownerUserId.Value, shortCode, domain, ct);
        if (link is null)
            return TypedResults.NotFound();

        return TypedResults.Ok(ToResponse(link, link.Domain, request.Scheme, request.Host.Host));
    }

    private static async Task<IResult> UpdateLinkAsync(
        string shortCode,
        UpdateLinkRequest body,
        string? domain,
        HttpRequest request,
        AppDbContext db,
        UserIdentityService identity,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var ownerUserId = await identity.ResolveOwnerUserIdAsync(user);
        if (ownerUserId is null)
            return TypedResults.Unauthorized();

        var link = await ResolveOwnedLinkAsync(db, ownerUserId.Value, shortCode, domain, ct);
        if (link is null)
            return TypedResults.NotFound();

        var errors = new Dictionary<string, string[]>();
        var domainId = link.DomainId;
        Domain? resolvedDomain = link.Domain;

        if (body.Domain is not null)
        {
            if (string.IsNullOrWhiteSpace(body.Domain))
            {
                domainId = null;
                resolvedDomain = null;
            }
            else
            {
                var hostname = body.Domain.Trim().ToLowerInvariant();
                resolvedDomain = await db.Domains.FirstOrDefaultAsync(
                    d => d.Hostname == hostname && d.OwnerUserId == ownerUserId && d.IsVerified, ct);
                if (resolvedDomain is null)
                    errors["domain"] = ["Unknown, unowned or unverified domain."];
                else
                    domainId = resolvedDomain.Id;
            }
        }

        var slug = link.ShortCode;
        if (body.Slug is not null)
        {
            var newSlug = body.Slug.Trim();
            if (newSlug.Length == 0 || !ShortLinkCodes.IsValidSlug(newSlug))
            {
                errors["slug"] = ["Must be 1–64 chars: letters, digits, '-' or '_', starting with a letter or digit."];
            }
            else
            {
                slug = newSlug;
            }
        }

        if (body.Url is not null)
        {
            var newUrl = body.Url.Trim();
            if (!Uri.TryCreate(newUrl, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
                errors["url"] = ["Must be an absolute http(s) URL."];
            else
                link.LongUrl = newUrl;
        }

        if (slug != link.ShortCode || domainId != link.DomainId)
        {
            var collides = await db.ShortenedUrls.AnyAsync(
                l => l.Id != link.Id && l.DomainId == domainId && l.ShortCode == slug, ct);
            if (collides)
                return TypedResults.Conflict();
        }

        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        link.ShortCode = slug;
        link.DomainId = domainId;
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(ToResponse(link, resolvedDomain, request.Scheme, request.Host.Host));
    }

    private static async Task<IResult> DeleteLinkAsync(
        string shortCode,
        string? domain,
        AppDbContext db,
        UserIdentityService identity,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var ownerUserId = await identity.ResolveOwnerUserIdAsync(user);
        if (ownerUserId is null)
            return TypedResults.Unauthorized();

        var link = await ResolveOwnedLinkAsync(db, ownerUserId.Value, shortCode, domain, ct);
        if (link is null)
            return TypedResults.NotFound();

        db.ShortenedUrls.Remove(link);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> GetLinkClicksAsync(
        string shortCode,
        string? domain,
        int? page,
        int? pageSize,
        AppDbContext db,
        UserIdentityService identity,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var ownerUserId = await identity.ResolveOwnerUserIdAsync(user);
        if (ownerUserId is null)
            return TypedResults.Unauthorized();

        var link = await ResolveOwnedLinkAsync(db, ownerUserId.Value, shortCode, domain, ct);
        if (link is null)
            return TypedResults.NotFound();

        var p = Math.Max(1, page ?? 1);
        var ps = Math.Clamp(pageSize ?? 20, 1, 100);

        var clickQuery = db.ClickEvents.Where(e => e.ShortenedUrlId == link.Id);
        var total = await clickQuery.CountAsync(ct);
        var rows = await clickQuery
            .OrderByDescending(e => e.ClickedAtUtc)
            .Skip((p - 1) * ps)
            .Take(ps)
            .Select(e => new ApiClickRow(
                e.Id, link.ShortCode, e.CountryCode, e.CountryName, e.CityName, e.Browser, e.BrowserVersion,
                e.OperatingSystem, e.OSVersion, e.Referer, e.IpAddress, e.DeviceFamily, e.ClickedAtUtc))
            .ToListAsync(ct);

        return TypedResults.Ok(new ClickListResponse(rows, p, ps, total));
    }

    /// <summary>
    /// Resolves a link owned by <paramref name="ownerUserId"/>. When <paramref name="domain"/>
    /// is supplied it filters by that domain; otherwise a default-domain match wins over
    /// an ambiguous multi-domain one.
    /// </summary>
    private static async Task<ShortenedUrl?> ResolveOwnedLinkAsync(
        AppDbContext db, long ownerUserId, string shortCode, string? domain, CancellationToken ct)
    {
        var query = db.ShortenedUrls
            .Include(l => l.Domain)
            .Where(l => l.OwnerUserId == ownerUserId && l.ShortCode == shortCode);

        if (!string.IsNullOrEmpty(domain))
        {
            query = domain == "default"
                ? query.Where(l => l.DomainId == null)
                : query.Where(l => l.Domain != null && l.Domain.Hostname == domain);
            return await query.FirstOrDefaultAsync(ct);
        }

        var matches = await query.ToListAsync(ct);
        if (matches.Count <= 1)
            return matches.FirstOrDefault();

        return matches.FirstOrDefault(l => l.DomainId == null) ?? matches[0];
    }

    private static LinkResponse ToResponse(ShortenedUrl link, Domain? domain, string scheme, string defaultHost)
    {
        var host = domain?.Hostname ?? defaultHost;
        return new LinkResponse(
            link.ShortCode,
            $"{scheme}://{host}/{link.ShortCode}",
            link.LongUrl,
            domain?.Hostname,
            link.ClickCount,
            link.CreatedAtUtc);
    }
}
