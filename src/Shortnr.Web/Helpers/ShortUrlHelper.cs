using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;

namespace Shortnr.Web.Helpers;

/// <summary>
/// Shared short-URL resolution and construction for surfaces that need the
/// domain-scoped URL of a link (QR codes, display cells).
/// </summary>
public static class ShortUrlHelper
{
    /// <summary>
    /// Finds the link for a short code, preferring a match on the request host's
    /// verified domain (mirroring the redirect endpoint) and falling back to a
    /// bare-code lookup so links on custom domains still resolve when the QR page
    /// or PNG is requested from the instance's own host.
    /// </summary>
    public static async Task<ShortenedUrl?> ResolveAsync(AppDbContext db, string? requestHost, string shortCode)
    {
        ShortenedUrl? link = null;

        if (!string.IsNullOrWhiteSpace(requestHost))
        {
            var domain = await db.Domains.FirstOrDefaultAsync(d =>
                d.Hostname == requestHost && d.IsVerified);
            if (domain is not null)
            {
                link = await db.ShortenedUrls
                    .AsNoTracking()
                    .Include(l => l.Domain)
                    .FirstOrDefaultAsync(l => l.DomainId == domain.Id && l.ShortCode == shortCode);
            }
        }

        return link ?? await db.ShortenedUrls
            .AsNoTracking()
            .Include(l => l.Domain)
            .FirstOrDefaultAsync(l => l.ShortCode == shortCode);
    }

    /// <summary>
    /// Builds the absolute URL for a link, using its verified custom-domain
    /// hostname when present and falling back to the request host.
    /// </summary>
    public static string Build(string scheme, string requestHost, ShortenedUrl link)
    {
        var host = link.Domain is { IsVerified: true, Hostname.Length: > 0 } domain
            ? domain.Hostname
            : requestHost;
        return $"{scheme}://{host}/{link.ShortCode}";
    }

    /// <summary>
    /// Display text for a link, e.g. <c>go.example.com/abc123</c> for a
    /// custom-domain link or <c>/abc123</c> for a default-host link.
    /// </summary>
    public static string DisplayText(string? hostname, string shortCode) =>
        hostname is { Length: > 0 } host ? $"{host}/{shortCode}" : $"/{shortCode}";

    /// <summary>
    /// Href for a link, e.g. <c>https://go.example.com/abc123</c> for a
    /// custom-domain link or <c>/abc123</c> for a default-host link.
    /// </summary>
    public static string DisplayHref(string scheme, string? hostname, string shortCode) =>
        hostname is { Length: > 0 } host ? $"{scheme}://{host}/{shortCode}" : $"/{shortCode}";
}
