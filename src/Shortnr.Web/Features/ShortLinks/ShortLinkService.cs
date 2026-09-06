using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Web.Features.Webhooks;

namespace Shortnr.Web.Features.ShortLinks;

/// <summary>Everything a caller needs to decide to create a link, minus the presentation.</summary>
public sealed record NewLinkRequest(
    string Url,
    string? Slug,
    long? DomainId,
    long? OwnerUserId,
    long? WorkspaceId,
    ShortenedUrlMetadata? Metadata = null);

/// <summary>
/// The result of a create attempt. Callers translate these into their own error
/// surface — problem details for the REST API, a string for MCP, a partial for
/// the Razor pages — so the shared code stays free of presentation concerns.
/// </summary>
public abstract record CreateLinkOutcome
{
    public sealed record Created(ShortenedUrl Link) : CreateLinkOutcome;
    public sealed record InvalidUrl : CreateLinkOutcome;
    public sealed record InvalidSlug : CreateLinkOutcome;
    public sealed record SlugTaken(string Slug) : CreateLinkOutcome;
}

/// <summary>
/// The single place a <see cref="ShortenedUrl"/> is created or deleted. The REST
/// API, the MCP tools, the home page and the bio editor all funnel through here
/// so the ownership rule and the webhook dispatch cannot drift apart again.
/// </summary>
public sealed class ShortLinkService(
    AppDbContext db,
    WebhookEventDispatcher webhooks,
    IHttpContextAccessor httpContextAccessor)
{
    public static bool IsAbsoluteHttpUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    /// <summary>The owner's verified default domain, or null when they have none.</summary>
    public Task<Domain?> ResolveDefaultDomainAsync(long? ownerUserId, CancellationToken ct = default) =>
        ownerUserId is null
            ? Task.FromResult<Domain?>(null)
            : db.Domains.FirstOrDefaultAsync(
                d => d.OwnerUserId == ownerUserId && d.IsVerified && d.IsDefault, ct);

    /// <summary>A verified domain the owner controls, matched case-insensitively by hostname.</summary>
    public Task<Domain?> ResolveVerifiedDomainAsync(string hostname, long? ownerUserId, CancellationToken ct = default)
    {
        var host = hostname.Trim().ToLowerInvariant();
        return db.Domains.FirstOrDefaultAsync(
            d => d.Hostname == host && d.OwnerUserId == ownerUserId && d.IsVerified, ct);
    }

    public Task<bool> SlugExistsAsync(long? domainId, string slug, CancellationToken ct = default) =>
        db.ShortenedUrls.AnyAsync(l => l.DomainId == domainId && l.ShortCode == slug, ct);

    public Task<string> GenerateCodeAsync(long? domainId, CancellationToken ct = default) =>
        ShortLinkCodes.GenerateUniqueCodeAsync(code => SlugExistsAsync(domainId, code, ct));

    /// <summary>
    /// Validates, persists and announces a new link. A link belongs either to a
    /// workspace or to a user, never both — that XOR is enforced here rather than
    /// at each call site.
    /// </summary>
    public async Task<CreateLinkOutcome> CreateAsync(NewLinkRequest request, CancellationToken ct = default)
    {
        var url = request.Url.Trim();
        if (!IsAbsoluteHttpUrl(url))
            return new CreateLinkOutcome.InvalidUrl();

        var slug = request.Slug?.Trim() ?? "";
        string shortCode;
        if (slug.Length > 0)
        {
            if (!ShortLinkCodes.IsValidSlug(slug))
                return new CreateLinkOutcome.InvalidSlug();
            if (await SlugExistsAsync(request.DomainId, slug, ct))
                return new CreateLinkOutcome.SlugTaken(slug);
            shortCode = slug;
        }
        else
        {
            shortCode = await GenerateCodeAsync(request.DomainId, ct);
        }

        var link = new ShortenedUrl
        {
            LongUrl = url,
            ShortCode = shortCode,
            DomainId = request.DomainId,
            OwnerUserId = request.WorkspaceId is not null ? null : request.OwnerUserId,
            WorkspaceId = request.WorkspaceId,
            CreatedAtUtc = DateTime.UtcNow,
            Metadata = request.Metadata
        };

        db.ShortenedUrls.Add(link);
        await db.SaveChangesAsync(ct);

        var (scheme, host) = ResolveRequestOrigin();
        await webhooks.DispatchLinkCreatedAsync(link, scheme, host);

        return new CreateLinkOutcome.Created(link);
    }

    /// <summary>Removes a link, announcing it before the row disappears.</summary>
    public async Task DeleteAsync(ShortenedUrl link, CancellationToken ct = default)
    {
        var (scheme, host) = ResolveRequestOrigin();
        await webhooks.DispatchLinkDeletedAsync(link, scheme, host);

        db.ShortenedUrls.Remove(link);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// The scheme and host webhook payloads use for links without their own domain.
    /// Falls back to the instance host when there is no ambient request.
    /// </summary>
    private (string Scheme, string Host) ResolveRequestOrigin()
    {
        var request = httpContextAccessor.HttpContext?.Request;
        return (request?.Scheme ?? "https", request?.Host.Host ?? "localhost");
    }
}
