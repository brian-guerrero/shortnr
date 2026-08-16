using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;

namespace Shortnr.Web.Features.Api;

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
            .RequireAuthorization(ApiKeyScopes.LinksWrite)
            .Produces<LinkResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status409Conflict);

        group.MapGet("/links", ListLinksAsync)
            .WithName("ListLinks")
            .WithSummary("List short links")
            .WithDescription("Paginated list scoped to the authenticated key's owner. Filters: domain (hostname or 'default'), workspace (slug), campaign (case-insensitive substring match on UTM campaign), from, to.")
            .RequireAuthorization(ApiKeyScopes.LinksRead)
            .Produces<LinkListResponse>();

        group.MapGet("/links/{shortCode}", GetLinkAsync)
            .WithName("GetLink")
            .WithSummary("Get a short link by code")
            .RequireAuthorization(ApiKeyScopes.LinksRead)
            .Produces<LinkResponse>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapPut("/links/{shortCode}", UpdateLinkAsync)
            .WithName("UpdateLink")
            .WithSummary("Update a short link")
            .WithDescription("Omitted fields keep their current value. Changing slug, domain, or workspace is subject to uniqueness rules.")
            .RequireAuthorization(ApiKeyScopes.LinksWrite)
            .Produces<LinkResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        group.MapPatch("/links/{shortCode}", UpdateLinkAsync)
            .WithName("PatchLink")
            .WithSummary("Partially update a short link")
            .WithDescription("PATCH alias of PUT — updates destination URL, slug, tags, title and description. Omitted fields keep their current value.")
            .RequireAuthorization(ApiKeyScopes.LinksWrite)
            .Produces<LinkResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        group.MapPost("/links/{shortCode}/archive", ArchiveLinkAsync)
            .WithName("ArchiveLink")
            .WithSummary("Archive a short link")
            .WithDescription("Archived links stop redirecting (HTTP 410) but are not deleted. Archiving an already-archived link is a no-op.")
            .RequireAuthorization(ApiKeyScopes.LinksWrite)
            .Produces<LinkResponse>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/links/{shortCode}/unarchive", UnarchiveLinkAsync)
            .WithName("UnarchiveLink")
            .WithSummary("Restore an archived short link")
            .WithDescription("Restores a link's redirect so it resolves again. Unarchiving an active link is a no-op.")
            .RequireAuthorization(ApiKeyScopes.LinksWrite)
            .Produces<LinkResponse>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/links/{shortCode}/transfer", TransferLinkAsync)
            .WithName("TransferLink")
            .WithSummary("Transfer a short link to another workspace")
            .WithDescription("Moves the link into the target workspace. The caller must be a member of both the source and the target workspace, otherwise a 403 is returned.")
            .RequireAuthorization(ApiKeyScopes.LinksWrite)
            .Produces<LinkResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapDelete("/links/{shortCode}", DeleteLinkAsync)
            .WithName("DeleteLink")
            .WithSummary("Delete a short link")
            .RequireAuthorization(ApiKeyScopes.LinksWrite)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/links/{shortCode}/clicks", GetLinkClicksAsync)
            .WithName("GetLinkClicks")
            .WithSummary("List click events for a short link")
            .WithDescription("Paginated click events, newest first.")
            .RequireAuthorization(ApiKeyScopes.LinksRead)
            .Produces<ClickListResponse>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/pixel-snippets", ListPixelSnippetsAsync)
            .WithName("ListPixelSnippets")
            .WithSummary("List available retargeting pixel snippets")
            .WithDescription("Names to pass as links.metadata.pixelSnippet on create/update.")
            .RequireAuthorization(ApiKeyScopes.LinksRead)
            .Produces<IReadOnlyList<PixelSnippetResponse>>();
    }

    private static async Task<IResult> CreateLinkAsync(
        CreateLinkRequest body,
        HttpRequest request,
        AppDbContext db,
        UserIdentityService identity,
        WorkspaceService workspaceService,
        WorkspaceAuthorizationService workspaceAuth,
        WebhookEventDispatcher webhookDispatcher,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var ownerUserId = await identity.ResolveOwnerUserIdAsync(user);
        if (ownerUserId is null)
            return TypedResults.Unauthorized();

        var workspaceId = (long?)null;
        if (body.Workspace is { Length: > 0 } ws)
        {
            var workspace = await workspaceService.GetWorkspaceBySlugAsync(ws);
            if (workspace is null || !await workspaceAuth.CanCreateLinkAsync(workspace.Id, ownerUserId))
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["workspace"] = ["Unknown workspace or insufficient permission."] });
            workspaceId = workspace.Id;
        }

        var url = body.Url?.Trim() ?? "";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["url"] = ["Must be an absolute http(s) URL."] });

        var utm = new UtmParameters(
            body.Metadata?.UtmSource, body.Metadata?.UtmMedium, body.Metadata?.UtmCampaign,
            body.Metadata?.UtmTerm, body.Metadata?.UtmContent);
        if (!utm.IsEmpty)
            url = UtmBuilder.AppendUtm(url, utm);

        var (pixelSnippetId, pixelValue, pixelError) = await ResolvePixelSelectionAsync(
            db, body.Metadata?.PixelSnippet, body.Metadata?.PixelId, body.Metadata?.PixelSnippetHtml, ct);
        if (pixelError is not null)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["metadata.pixelSnippet"] = [pixelError] });

        var iosDeepLink = body.Metadata?.IosDeepLink?.Trim();
        var androidDeepLink = body.Metadata?.AndroidDeepLink?.Trim();

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
            OwnerUserId = workspaceId is not null ? null : ownerUserId,
            WorkspaceId = workspaceId,
            PreviewTheme = PreviewThemes.IsValid(body.PreviewTheme) ? body.PreviewTheme : null,
            CreatedAtUtc = DateTime.UtcNow
        };

        var hasMetadata = !utm.IsEmpty || pixelSnippetId is not null
            || !string.IsNullOrWhiteSpace(iosDeepLink) || !string.IsNullOrWhiteSpace(androidDeepLink);
        if (hasMetadata)
        {
            link.Metadata = new ShortenedUrlMetadata
            {
                UtmSource = utm.Source,
                UtmMedium = utm.Medium,
                UtmCampaign = utm.Campaign,
                UtmTerm = utm.Term,
                UtmContent = utm.Content,
                PixelSnippetId = pixelSnippetId,
                PixelId = pixelValue,
                IosDeepLink = string.IsNullOrWhiteSpace(iosDeepLink) ? null : iosDeepLink,
                AndroidDeepLink = string.IsNullOrWhiteSpace(androidDeepLink) ? null : androidDeepLink
            };
        }

        db.ShortenedUrls.Add(link);
        await db.SaveChangesAsync(ct);

        await webhookDispatcher.DispatchLinkCreatedAsync(link, request.Scheme, request.Host.Host);

        var workspaceSlug = workspaceId is not null
            ? (await db.Workspaces.Where(w => w.Id == workspaceId).Select(w => w.Slug).FirstOrDefaultAsync(ct))
            : null;
        var pixelSnippetName = await ResolvePixelNameAsync(db, pixelSnippetId, ct);
        return TypedResults.Created(
            $"/api/v1/links/{shortCode}",
            ToResponse(link, domain, request.Scheme, request.Host.Host, workspaceSlug, pixelSnippetName));
    }

    private static async Task<IResult> ListLinksAsync(
        int? page,
        int? pageSize,
        string? domain,
        string? workspace,
        string? campaign,
        HttpRequest request,
        AppDbContext db,
        UserIdentityService identity,
        WorkspaceService workspaceService,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var ownerUserId = await identity.ResolveOwnerUserIdAsync(user);
        if (ownerUserId is null)
            return TypedResults.Unauthorized();

        var p = Math.Max(1, page ?? 1);
        var ps = Math.Clamp(pageSize ?? 20, 1, 100);

        var query = db.ShortenedUrls.AsQueryable();
        if (workspace is { Length: > 0 } ws)
        {
            var wsEntity = await workspaceService.GetWorkspaceBySlugAsync(ws);
            if (wsEntity is null || !await workspaceService.IsMemberAsync(wsEntity.Id, ownerUserId.Value))
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["workspace"] = ["Unknown workspace."] });
            query = query.Where(l => l.WorkspaceId == wsEntity.Id);
        }
        else
        {
            query = query.Where(l => l.OwnerUserId == ownerUserId);
        }

        if (!string.IsNullOrEmpty(domain))
        {
            query = domain == "default"
                ? query.Where(l => l.DomainId == null)
                : query.Where(l => l.Domain != null && l.Domain.Hostname == domain);
        }

        if (!string.IsNullOrWhiteSpace(campaign))
        {
            var c = campaign.Trim();
            query = query.Where(l => l.Metadata != null && l.Metadata.UtmCampaign != null && l.Metadata.UtmCampaign.Contains(c));
        }

        var total = await query.CountAsync(ct);
        var links = await query
            .AsNoTracking()
            .OrderByDescending(l => l.CreatedAtUtc)
            .Include(l => l.Domain)
            .Include(l => l.Workspace)
            .Include(l => l.Tags)
            .Include(l => l.Metadata)
            .ThenInclude(m => m!.PixelSnippet)
            .Skip((p - 1) * ps)
            .Take(ps)
            .ToListAsync(ct);

        return TypedResults.Ok(new LinkListResponse(
            links.Select(l => ToResponse(l, l.Domain, request.Scheme, request.Host.Host, l.Workspace?.Slug)).ToList(),
            p, ps, total));
    }

    private static async Task<IResult> GetLinkAsync(
        string shortCode,
        string? domain,
        string? workspace,
        HttpRequest request,
        AppDbContext db,
        UserIdentityService identity,
        WorkspaceService workspaceService,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var ownerUserId = await identity.ResolveOwnerUserIdAsync(user);
        if (ownerUserId is null)
            return TypedResults.Unauthorized();

        var link = await ResolveOwnedLinkAsync(db, ownerUserId.Value, shortCode, domain, workspace, workspaceService, ct);
        if (link is null)
            return TypedResults.NotFound();

        return TypedResults.Ok(ToResponse(link, link.Domain, request.Scheme, request.Host.Host, link.Workspace?.Slug));
    }

    private static async Task<IResult> UpdateLinkAsync(
        string shortCode,
        UpdateLinkRequest body,
        string? domain,
        string? workspace,
        HttpRequest request,
        AppDbContext db,
        UserIdentityService identity,
        WorkspaceService workspaceService,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var ownerUserId = await identity.ResolveOwnerUserIdAsync(user);
        if (ownerUserId is null)
            return TypedResults.Unauthorized();

        var link = await ResolveOwnedLinkAsync(db, ownerUserId.Value, shortCode, domain, workspace, workspaceService, ct);
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

        if (body.Title is not null)
            link.Title = string.IsNullOrWhiteSpace(body.Title) ? null : body.Title.Trim();

        if (body.Description is not null)
            link.Description = string.IsNullOrWhiteSpace(body.Description) ? null : body.Description.Trim();

        if (body.PreviewTheme is not null)
            link.PreviewTheme = PreviewThemes.IsValid(body.PreviewTheme) ? body.PreviewTheme.Trim() : null;

        // Campaign metadata: unlike the fields above, each sub-field independently
        // follows the omit-keeps/empty-clears convention, merged against whatever
        // the link already has — a caller can touch just metadata.utmCampaign
        // without resending the rest.
        var metadataTouched = body.Metadata is not null && (
            body.Metadata.UtmSource is not null || body.Metadata.UtmMedium is not null || body.Metadata.UtmCampaign is not null
            || body.Metadata.UtmTerm is not null || body.Metadata.UtmContent is not null
            || body.Metadata.PixelSnippet is not null || body.Metadata.PixelId is not null || body.Metadata.PixelSnippetHtml is not null
            || body.Metadata.IosDeepLink is not null || body.Metadata.AndroidDeepLink is not null);

        var mergedUtm = new UtmParameters(null, null, null, null, null);
        long? mergedPixelSnippetId = null;
        string? mergedPixelValue = null;
        string? mergedIos = null;
        string? mergedAndroid = null;

        if (metadataTouched)
        {
            var existing = link.Metadata;
            mergedUtm = new UtmParameters(
                MergeNullable(body.Metadata!.UtmSource, existing?.UtmSource),
                MergeNullable(body.Metadata.UtmMedium, existing?.UtmMedium),
                MergeNullable(body.Metadata.UtmCampaign, existing?.UtmCampaign),
                MergeNullable(body.Metadata.UtmTerm, existing?.UtmTerm),
                MergeNullable(body.Metadata.UtmContent, existing?.UtmContent));

            mergedPixelSnippetId = existing?.PixelSnippetId;
            mergedPixelValue = existing?.PixelId;
            if (body.Metadata.PixelSnippet is not null)
            {
                var (id, value, pixelError) = await ResolvePixelSelectionAsync(
                    db, body.Metadata.PixelSnippet, body.Metadata.PixelId, body.Metadata.PixelSnippetHtml, ct);
                if (pixelError is not null)
                    errors["metadata.pixelSnippet"] = [pixelError];
                mergedPixelSnippetId = id;
                mergedPixelValue = value;
            }
            else if (body.Metadata.PixelId is not null || body.Metadata.PixelSnippetHtml is not null)
            {
                if (mergedPixelSnippetId is null)
                {
                    errors["metadata.pixelId"] = ["No pixel snippet is currently attached to this link; set metadata.pixelSnippet to choose one."];
                }
                else
                {
                    var currentSnippet = existing?.PixelSnippet
                        ?? await db.PixelSnippets.FirstOrDefaultAsync(p => p.Id == mergedPixelSnippetId, ct);
                    mergedPixelValue = (currentSnippet?.IsCustom == true ? body.Metadata.PixelSnippetHtml : body.Metadata.PixelId)?.Trim();
                }
            }

            mergedIos = MergeNullable(body.Metadata.IosDeepLink, existing?.IosDeepLink);
            mergedAndroid = MergeNullable(body.Metadata.AndroidDeepLink, existing?.AndroidDeepLink);
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
        link.UpdatedAtUtc = DateTime.UtcNow;

        if (body.Tags is not null)
            await ReplaceTagsAsync(db, link, body.Tags, ct);

        if (metadataTouched)
        {
            if (!mergedUtm.IsEmpty)
                link.LongUrl = UtmBuilder.AppendUtm(link.LongUrl, mergedUtm);

            var hasMetadata = !mergedUtm.IsEmpty || mergedPixelSnippetId is not null
                || mergedIos is not null || mergedAndroid is not null;
            if (hasMetadata)
            {
                if (link.Metadata is null)
                {
                    link.Metadata = new ShortenedUrlMetadata { ShortenedUrlId = link.Id };
                    db.ShortenedUrlMetadatas.Add(link.Metadata);
                }
                link.Metadata.UtmSource = mergedUtm.Source;
                link.Metadata.UtmMedium = mergedUtm.Medium;
                link.Metadata.UtmCampaign = mergedUtm.Campaign;
                link.Metadata.UtmTerm = mergedUtm.Term;
                link.Metadata.UtmContent = mergedUtm.Content;
                link.Metadata.PixelSnippetId = mergedPixelSnippetId;
                link.Metadata.PixelId = mergedPixelValue;
                link.Metadata.IosDeepLink = mergedIos;
                link.Metadata.AndroidDeepLink = mergedAndroid;
            }
            else if (link.Metadata is not null)
            {
                db.ShortenedUrlMetadatas.Remove(link.Metadata);
                link.Metadata = null;
            }
        }

        await db.SaveChangesAsync(ct);

        var resultPixelName = link.Metadata?.PixelSnippet?.Name
            ?? await ResolvePixelNameAsync(db, link.Metadata?.PixelSnippetId, ct);

        return TypedResults.Ok(ToResponse(link, resolvedDomain, request.Scheme, request.Host.Host, link.Workspace?.Slug, resultPixelName));
    }

    private static async Task<IResult> ArchiveLinkAsync(
        string shortCode,
        string? domain,
        string? workspace,
        HttpRequest request,
        AppDbContext db,
        UserIdentityService identity,
        WorkspaceService workspaceService,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var ownerUserId = await identity.ResolveOwnerUserIdAsync(user);
        if (ownerUserId is null)
            return TypedResults.Unauthorized();

        var link = await ResolveOwnedLinkAsync(db, ownerUserId.Value, shortCode, domain, workspace, workspaceService, ct);
        if (link is null)
            return TypedResults.NotFound();

        if (link.ArchivedAtUtc is null)
        {
            link.ArchivedAtUtc = DateTime.UtcNow;
            link.UpdatedAtUtc = link.ArchivedAtUtc;
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.Ok(ToResponse(link, link.Domain, request.Scheme, request.Host.Host, link.Workspace?.Slug));
    }

    private static async Task<IResult> UnarchiveLinkAsync(
        string shortCode,
        string? domain,
        string? workspace,
        HttpRequest request,
        AppDbContext db,
        UserIdentityService identity,
        WorkspaceService workspaceService,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var ownerUserId = await identity.ResolveOwnerUserIdAsync(user);
        if (ownerUserId is null)
            return TypedResults.Unauthorized();

        var link = await ResolveOwnedLinkAsync(db, ownerUserId.Value, shortCode, domain, workspace, workspaceService, ct);
        if (link is null)
            return TypedResults.NotFound();

        if (link.ArchivedAtUtc is not null)
        {
            link.ArchivedAtUtc = null;
            link.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.Ok(ToResponse(link, link.Domain, request.Scheme, request.Host.Host, link.Workspace?.Slug));
    }

    private static async Task<IResult> TransferLinkAsync(
        string shortCode,
        TransferLinkRequest body,
        string? domain,
        string? workspace,
        HttpRequest request,
        AppDbContext db,
        UserIdentityService identity,
        WorkspaceService workspaceService,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var ownerUserId = await identity.ResolveOwnerUserIdAsync(user);
        if (ownerUserId is null)
            return TypedResults.Unauthorized();

        var link = await ResolveOwnedLinkAsync(db, ownerUserId.Value, shortCode, domain, workspace, workspaceService, ct);
        if (link is null)
            return TypedResults.NotFound();

        var targetSlug = body.Workspace.Trim();
        if (targetSlug.Length == 0 || !WorkspaceService.IsValidSlug(targetSlug))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["workspace"] = ["Enter a valid workspace slug."] });

        var target = await workspaceService.GetWorkspaceBySlugAsync(targetSlug);
        if (target is null || !await workspaceService.IsMemberAsync(target.Id, ownerUserId.Value))
            return TypedResults.StatusCode(StatusCodes.Status403Forbidden);

        var sourceWorkspaceId = link.WorkspaceId;
        if (sourceWorkspaceId is not null && !await workspaceService.IsMemberAsync(sourceWorkspaceId.Value, ownerUserId.Value))
            return TypedResults.StatusCode(StatusCodes.Status403Forbidden);

        link.WorkspaceId = target.Id;
        link.OwnerUserId = null;
        link.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(ToResponse(link, link.Domain, request.Scheme, request.Host.Host, target.Slug));
    }

    private static async Task<IResult> DeleteLinkAsync(
        string shortCode,
        string? domain,
        string? workspace,
        HttpRequest request,
        AppDbContext db,
        UserIdentityService identity,
        WorkspaceService workspaceService,
        WebhookEventDispatcher webhookDispatcher,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var ownerUserId = await identity.ResolveOwnerUserIdAsync(user);
        if (ownerUserId is null)
            return TypedResults.Unauthorized();

        var link = await ResolveOwnedLinkAsync(db, ownerUserId.Value, shortCode, domain, workspace, workspaceService, ct);
        if (link is null)
            return TypedResults.NotFound();

        await webhookDispatcher.DispatchLinkDeletedAsync(link, request.Scheme, request.Host.Host);

        db.ShortenedUrls.Remove(link);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> GetLinkClicksAsync(
        string shortCode,
        string? domain,
        string? workspace,
        int? page,
        int? pageSize,
        AppDbContext db,
        UserIdentityService identity,
        WorkspaceService workspaceService,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var ownerUserId = await identity.ResolveOwnerUserIdAsync(user);
        if (ownerUserId is null)
            return TypedResults.Unauthorized();

        var link = await ResolveOwnedLinkAsync(db, ownerUserId.Value, shortCode, domain, workspace, workspaceService, ct);
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

    private static async Task<IResult> ListPixelSnippetsAsync(
        AppDbContext db,
        UserIdentityService identity,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var ownerUserId = await identity.ResolveOwnerUserIdAsync(user);
        if (ownerUserId is null)
            return TypedResults.Unauthorized();

        var snippets = await db.PixelSnippets
            .OrderBy(p => p.Id)
            .Select(p => new PixelSnippetResponse(p.Name, p.IsCustom))
            .ToListAsync(ct);

        return TypedResults.Ok<IReadOnlyList<PixelSnippetResponse>>(snippets);
    }

    private static async Task<ShortenedUrl?> ResolveOwnedLinkAsync(
        AppDbContext db, long ownerUserId, string shortCode, string? domain, string? workspace,
        WorkspaceService workspaceService, CancellationToken ct)
    {
        var query = db.ShortenedUrls
            .Include(l => l.Domain)
            .Include(l => l.Workspace)
            .Include(l => l.Tags)
            .Include(l => l.Metadata)
            .ThenInclude(m => m!.PixelSnippet)
            .AsQueryable();

        if (workspace is { Length: > 0 } ws)
        {
            var wsEntity = await workspaceService.GetWorkspaceBySlugAsync(ws);
            if (wsEntity is null || !await workspaceService.IsMemberAsync(wsEntity.Id, ownerUserId))
                return null;
            query = query.Where(l => l.WorkspaceId == wsEntity.Id && l.ShortCode == shortCode);
        }
        else
        {
            query = query.Where(l => l.OwnerUserId == ownerUserId && l.ShortCode == shortCode);
        }

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

    private static LinkResponse ToResponse(ShortenedUrl link, Domain? domain, string scheme, string defaultHost,
        string? workspaceSlug = null, string? pixelSnippetNameOverride = null)
    {
        var host = domain?.Hostname ?? defaultHost;
        var metadata = link.Metadata is null
            ? null
            : new LinkMetadataResponse(
                link.Metadata.UtmSource, link.Metadata.UtmMedium, link.Metadata.UtmCampaign,
                link.Metadata.UtmTerm, link.Metadata.UtmContent,
                pixelSnippetNameOverride ?? link.Metadata.PixelSnippet?.Name,
                link.Metadata.PixelId, link.Metadata.IosDeepLink, link.Metadata.AndroidDeepLink);

        return new LinkResponse(
            link.ShortCode,
            $"{scheme}://{host}/{link.ShortCode}",
            link.LongUrl,
            domain?.Hostname,
            link.ClickCount,
            link.CreatedAtUtc,
            workspaceSlug,
            link.Tags?.Select(t => t.Name).OrderBy(n => n).ToList(),
            link.Title,
            link.Description,
            link.PreviewTheme,
            link.ArchivedAtUtc,
            link.UpdatedAtUtc,
            metadata);
    }

    private static async Task ReplaceTagsAsync(AppDbContext db, ShortenedUrl link, IReadOnlyList<string> tags, CancellationToken ct)
    {
        var existing = await db.ShortenedUrlTags
            .Where(t => t.ShortenedUrlId == link.Id)
            .ToListAsync(ct);
        db.ShortenedUrlTags.RemoveRange(existing);

        foreach (var raw in tags)
        {
            var name = raw.Trim();
            if (name.Length > 0)
            {
                db.ShortenedUrlTags.Add(new ShortenedUrlTag
                {
                    ShortenedUrlId = link.Id,
                    Name = name.Length > 128 ? name[..128] : name,
                    CreatedAtUtc = DateTime.UtcNow
                });
            }
        }
    }

    /// <summary>
    /// Resolves a metadata.pixelSnippet name (see GET /api/v1/pixel-snippets) into a
    /// PixelSnippet id + the value to store (pixelId for template snippets,
    /// pixelSnippetHtml for the custom snippet). A null/empty name means "no
    /// selection" and returns (null, null, null) rather than an error.
    /// </summary>
    private static async Task<(long? Id, string? Value, string? Error)> ResolvePixelSelectionAsync(
        AppDbContext db, string? pixelSnippetName, string? pixelId, string? pixelSnippetHtml, CancellationToken ct)
    {
        var name = pixelSnippetName?.Trim() ?? "";
        if (name.Length == 0)
            return (null, null, null);

        var snippet = await db.PixelSnippets.FirstOrDefaultAsync(p => p.Name.ToLower() == name.ToLower(), ct);
        if (snippet is null)
            return (null, null, $"No pixel snippet named '{name}'. See GET /api/v1/pixel-snippets for available names.");

        var value = (snippet.IsCustom ? pixelSnippetHtml : pixelId)?.Trim();
        if (string.IsNullOrWhiteSpace(value))
            return (null, null, snippet.IsCustom
                ? "metadata.pixelSnippetHtml is required when selecting the custom pixel snippet."
                : "metadata.pixelId is required when selecting a template pixel snippet.");

        return (snippet.Id, value, null);
    }

    private static async Task<string?> ResolvePixelNameAsync(AppDbContext db, long? pixelSnippetId, CancellationToken ct) =>
        pixelSnippetId is null ? null : (await db.PixelSnippets.FirstOrDefaultAsync(p => p.Id == pixelSnippetId, ct))?.Name;

    /// <summary>An update argument that's non-null replaces the current value (trimmed;
    /// empty clears it to null); a null argument means "leave unchanged".</summary>
    private static string? MergeNullable(string? provided, string? current) =>
        provided is not null ? (provided.Trim().Length > 0 ? provided.Trim() : null) : current;
}
