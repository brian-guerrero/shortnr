using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;

namespace Shortnr.Web.Pages;

public class IndexModel(AppDbContext db, UserIdentityService identity, ShortenRateLimiter shortenLimiter,
    ShortLinkService links) : PageModel
{
    public List<ShortenedUrl> RecentLinks { get; set; } = [];
    public List<PixelSnippet> PixelSnippets { get; set; } = [];
    public bool IsHtmxRequest { get; set; }
    public string? DefaultHostname { get; set; }
    public ActiveWorkspaceContext? Workspace { get; set; }

    public async Task OnGet()
    {
        IsHtmxRequest = Request.Headers["HX-Request"].Count > 0;
        var ownerUserId = await identity.ResolveOwnerUserIdAsync(User);
        Workspace = await identity.ResolveActiveWorkspaceContextAsync(User);
        var defaultDomain = await links.ResolveDefaultDomainAsync(ownerUserId);
        DefaultHostname = defaultDomain?.Hostname;
        RecentLinks = await RecentLinksAsync(ownerUserId, Workspace?.WorkspaceId);
        PixelSnippets = await db.PixelSnippets.OrderBy(p => p.Id).ToListAsync();
    }

    public async Task<IActionResult> OnPost()
    {
        if (!await shortenLimiter.TryAcquireAsync(HttpContext))
            return new StatusCodeResult(StatusCodes.Status429TooManyRequests);

        var url = Request.Form["url"].FirstOrDefault() ?? "";
        var slug = Request.Form["slug"].FirstOrDefault()?.Trim() ?? "";
        var utm = ReadUtmParameters();
        var pixelSnippetId = ReadPixelSnippetId();
        var pixelValue = await ResolvePixelValueAsync(pixelSnippetId,
            Request.Form["pixel_id"].FirstOrDefault(),
            Request.Form["pixel_snippet"].FirstOrDefault());
        var iosDeepLink = Request.Form["ios_deep_link"].FirstOrDefault()?.Trim() ?? "";
        var androidDeepLink = Request.Form["android_deep_link"].FirstOrDefault()?.Trim() ?? "";

        var ownerUserId = await identity.ResolveOwnerUserIdAsync(User);
        Workspace = await identity.ResolveActiveWorkspaceContextAsync(User);
        var workspaceId = Workspace?.WorkspaceId;
        var defaultDomain = await links.ResolveDefaultDomainAsync(ownerUserId);
        var domainId = defaultDomain?.Id;

        if (string.IsNullOrWhiteSpace(url))
            return await ErrorResultAsync("Enter a URL to shorten.", ownerUserId, workspaceId);

        if (!utm.IsEmpty)
            url = UtmBuilder.AppendUtm(url, utm);

        var metadata = BuildMetadata(utm, pixelSnippetId, pixelValue, iosDeepLink, androidDeepLink);

        // Without smart-link metadata an identical URL on the same domain is
        // reused rather than duplicated.
        if (slug.Length == 0 && metadata is null)
        {
            var existing = await db.ShortenedUrls.FirstOrDefaultAsync(l => l.DomainId == domainId && l.LongUrl == url);
            if (existing is not null)
                return await ResultAsync(defaultDomain, existing.ShortCode, ownerUserId, workspaceId);
        }

        var outcome = await links.CreateAsync(
            new NewLinkRequest(url, slug, domainId, ownerUserId, workspaceId, metadata));

        return outcome switch
        {
            CreateLinkOutcome.Created created =>
                await ResultAsync(defaultDomain, created.Link.ShortCode, ownerUserId, workspaceId),
            CreateLinkOutcome.InvalidSlug =>
                await ErrorResultAsync("Custom code must be 1–64 characters: letters, digits, '-' and '_', starting with a letter or digit.", ownerUserId, workspaceId),
            CreateLinkOutcome.SlugTaken taken =>
                await ErrorResultAsync($"The custom code '{taken.Slug}' is already taken.", ownerUserId, workspaceId),
            _ => await ErrorResultAsync("Enter a valid http(s) URL.", ownerUserId, workspaceId)
        };
    }

    private static ShortenedUrlMetadata? BuildMetadata(UtmParameters utm, long? pixelSnippetId, string? pixelValue,
        string iosDeepLink, string androidDeepLink)
    {
        if (utm.IsEmpty && pixelSnippetId is null && iosDeepLink.Length == 0 && androidDeepLink.Length == 0)
            return null;

        return new ShortenedUrlMetadata
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

    private async Task<IActionResult> ResultAsync(Domain? defaultDomain, string shortCode, long? ownerUserId, long? workspaceId)
    {
        var recentLinks = await RecentLinksAsync(ownerUserId, workspaceId);
        return Partial("Shared/_PostResult", new PostResultViewModel
        {
            ShortUrl = BuildShortUrl(defaultDomain, shortCode),
            ShortCode = shortCode,
            RecentLinks = recentLinks
        });
    }

    private async Task<IActionResult> ErrorResultAsync(string message, long? ownerUserId, long? workspaceId)
    {
        var defaultDomain = await links.ResolveDefaultDomainAsync(ownerUserId);
        var recentLinks = await RecentLinksAsync(ownerUserId, workspaceId);
        return Partial("Shared/_PostResult", new PostResultViewModel { HasError = true, ErrorMessage = message, RecentLinks = recentLinks });
    }

    private UtmParameters ReadUtmParameters() =>
        new(
            Source: Request.Form["utm_source"].FirstOrDefault(),
            Medium: Request.Form["utm_medium"].FirstOrDefault(),
            Campaign: Request.Form["utm_campaign"].FirstOrDefault(),
            Term: Request.Form["utm_term"].FirstOrDefault(),
            Content: Request.Form["utm_content"].FirstOrDefault());

    /// <summary>Parses the chosen pixel type into a PixelSnippet id, or null when none/invalid.</summary>
    private long? ReadPixelSnippetId()
    {
        var raw = Request.Form["pixel_type"].FirstOrDefault();
        return long.TryParse(raw, out var id) ? id : null;
    }

    /// <summary>
    /// Resolves the metadata's PixelId value: the pixel ID for template snippets,
    /// or the full pasted HTML for the custom snippet.
    /// </summary>
    private async Task<string?> ResolvePixelValueAsync(long? pixelSnippetId, string? pixelId, string? customSnippet)
    {
        if (pixelSnippetId is null)
            return null;

        var snippet = await db.PixelSnippets.FirstOrDefaultAsync(p => p.Id == pixelSnippetId);
        if (snippet is null)
            return null;

        return snippet.IsCustom ? customSnippet : pixelId;
    }

    private string BuildShortUrl(Domain? defaultDomain, string shortCode) =>
        $"{Request.Scheme}://{(defaultDomain?.Hostname ?? Request.Host.Host)}/{shortCode}";

    private Task<List<ShortenedUrl>> RecentLinksAsync(long? ownerUserId, long? workspaceId)
    {
        var query = db.ShortenedUrls
            .AsNoTracking()
            .Include(l => l.Domain)
            .ScopeToOwnerOrBareHost(ownerUserId, workspaceId);

        return query.OrderByDescending(l => l.CreatedAtUtc).Take(10).ToListAsync();
    }
}
