using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;

namespace Shortnr.Web.Pages;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly UserIdentityService _identity;
    private readonly ShortenRateLimiter _shortenLimiter;
    private readonly WebhookEventDispatcher _webhookDispatcher;

    public List<ShortenedUrl> RecentLinks { get; set; } = [];
    public List<PixelSnippet> PixelSnippets { get; set; } = [];
    public LinkAdvancedOptionsViewModel AdvancedOptions { get; set; } = new() { ShowCustomCode = true };
    public bool IsHtmxRequest { get; set; }
    public string? DefaultHostname { get; set; }
    public ActiveWorkspaceContext? Workspace { get; set; }

    public IndexModel(AppDbContext db, UserIdentityService identity, ShortenRateLimiter shortenLimiter, WebhookEventDispatcher webhookDispatcher)
    {
        _db = db;
        _identity = identity;
        _shortenLimiter = shortenLimiter;
        _webhookDispatcher = webhookDispatcher;
    }

    public async Task OnGet()
    {
        IsHtmxRequest = Request.Headers["HX-Request"].Count > 0;
        var ownerUserId = await _identity.ResolveOwnerUserIdAsync(User);
        Workspace = await _identity.ResolveActiveWorkspaceContextAsync(User);
        var defaultDomain = await ResolveDefaultDomainAsync(ownerUserId);
        DefaultHostname = defaultDomain?.Hostname;
        RecentLinks = await RecentLinksAsync(ownerUserId, Workspace?.WorkspaceId);
        PixelSnippets = await _db.PixelSnippets.OrderBy(p => p.Id).ToListAsync();
        AdvancedOptions = new LinkAdvancedOptionsViewModel
        {
            ShowCustomCode = true,
            PixelSnippets = PixelSnippets
        };
    }

    public async Task<IActionResult> OnPost()
    {
        if (!await _shortenLimiter.TryAcquireAsync(HttpContext))
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
        var previewTheme = Request.Form["preview_theme"].FirstOrDefault()?.Trim() ?? "";

        var ownerUserId = await _identity.ResolveOwnerUserIdAsync(User);
        Workspace = await _identity.ResolveActiveWorkspaceContextAsync(User);
        var workspaceId = Workspace?.WorkspaceId;
        var defaultDomain = await ResolveDefaultDomainAsync(ownerUserId);
        var domainId = defaultDomain?.Id;

        if (string.IsNullOrWhiteSpace(url))
            return await ErrorResultAsync("Enter a URL to shorten.", ownerUserId, workspaceId);

        if (!utm.IsEmpty)
            url = UtmBuilder.AppendUtm(url, utm);

        if (slug.Length > 0)
        {
            if (!ShortLinkCodes.IsValidSlug(slug))
                return await ErrorResultAsync("Custom code must be 1–64 characters: letters, digits, '-' and '_', starting with a letter or digit.", ownerUserId, workspaceId);

            var collides = await _db.ShortenedUrls.AnyAsync(l => l.DomainId == domainId && l.ShortCode == slug);
            if (collides)
                return await ErrorResultAsync($"The custom code '{slug}' is already taken.", ownerUserId, workspaceId);

            return await CreateAsync(url, slug, defaultDomain, ownerUserId, workspaceId, utm, pixelSnippetId, pixelValue, iosDeepLink, androidDeepLink, previewTheme);
        }

        var hasSmartLinkMetadata = !utm.IsEmpty || pixelSnippetId is not null
            || iosDeepLink.Length > 0 || androidDeepLink.Length > 0;
        if (!hasSmartLinkMetadata)
        {
            var existing = await _db.ShortenedUrls.FirstOrDefaultAsync(l => l.DomainId == domainId && l.LongUrl == url);
            if (existing is not null)
            {
                var baseUrl = BuildShortUrl(defaultDomain, existing.ShortCode);
                var recentLinks = await RecentLinksAsync(ownerUserId, workspaceId);
                return Partial("Shared/_PostResult", new PostResultViewModel { ShortUrl = baseUrl, ShortCode = existing.ShortCode, RecentLinks = recentLinks });
            }
        }

        return await CreateAsync(url, await ShortLinkCodes.GenerateUniqueCodeAsync(code =>
            _db.ShortenedUrls.AnyAsync(l => l.DomainId == domainId && l.ShortCode == code)), defaultDomain, ownerUserId, workspaceId, utm, pixelSnippetId, pixelValue, iosDeepLink, androidDeepLink, previewTheme);
    }

    private async Task<IActionResult> CreateAsync(string url, string shortCode, Domain? defaultDomain, long? ownerUserId, long? workspaceId, UtmParameters? utm = null, long? pixelSnippetId = null, string? pixelValue = null, string? iosDeepLink = null, string? androidDeepLink = null, string? previewTheme = null)
    {
        var shortened = new ShortenedUrl
        {
            LongUrl = url,
            ShortCode = shortCode,
            DomainId = defaultDomain?.Id,
            CreatedAtUtc = DateTime.UtcNow,
            OwnerUserId = workspaceId is not null ? null : ownerUserId,
            WorkspaceId = workspaceId,
            PreviewTheme = ThemeCatalog.IsValid(previewTheme) ? previewTheme : null
        };
        if (utm is not null && !utm.IsEmpty || pixelSnippetId is not null || iosDeepLink is not null && iosDeepLink.Length > 0 || androidDeepLink is not null && androidDeepLink.Length > 0)
        {
            shortened.Metadata = new ShortenedUrlMetadata
            {
                UtmSource = utm?.Source,
                UtmMedium = utm?.Medium,
                UtmCampaign = utm?.Campaign,
                UtmTerm = utm?.Term,
                UtmContent = utm?.Content,
                PixelSnippetId = pixelSnippetId,
                PixelId = pixelValue,
                IosDeepLink = string.IsNullOrWhiteSpace(iosDeepLink) ? null : iosDeepLink,
                AndroidDeepLink = string.IsNullOrWhiteSpace(androidDeepLink) ? null : androidDeepLink
            };
        }
        _db.ShortenedUrls.Add(shortened);
        await _db.SaveChangesAsync();

        await _webhookDispatcher.DispatchLinkCreatedAsync(shortened, Request.Scheme, Request.Host.Host);

        var recentLinks = await RecentLinksAsync(ownerUserId, workspaceId);
        var baseUrl = BuildShortUrl(defaultDomain, shortCode);
        return Partial("Shared/_PostResult", new PostResultViewModel { ShortUrl = baseUrl, ShortCode = shortCode, RecentLinks = recentLinks });
    }

    private async Task<IActionResult> ErrorResultAsync(string message, long? ownerUserId, long? workspaceId)
    {
        var defaultDomain = await ResolveDefaultDomainAsync(ownerUserId);
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

        var snippet = await _db.PixelSnippets.FirstOrDefaultAsync(p => p.Id == pixelSnippetId);
        if (snippet is null)
            return null;

        return snippet.IsCustom ? customSnippet : pixelId;
    }

    private async Task<Domain?> ResolveDefaultDomainAsync(long? ownerUserId)
    {        if (ownerUserId is null)
            return null;

        return await _db.Domains.FirstOrDefaultAsync(d =>
            d.OwnerUserId == ownerUserId && d.IsVerified && d.IsDefault);
    }

    private string BuildShortUrl(Domain? defaultDomain, string shortCode) =>
        $"{Request.Scheme}://{(defaultDomain?.Hostname ?? Request.Host.Host)}/{shortCode}";

    private Task<List<ShortenedUrl>> RecentLinksAsync(long? ownerUserId, long? workspaceId)
    {
        var query = _db.ShortenedUrls.AsNoTracking().Include(l => l.Domain).AsQueryable();

        if (workspaceId is not null)
            query = query.Where(l => l.WorkspaceId == workspaceId);
        else if (ownerUserId is not null)
            query = query.Where(l => l.OwnerUserId == ownerUserId);
        else
            query = query.Where(l => l.DomainId == null);

        return query.OrderByDescending(l => l.CreatedAtUtc).Take(10).ToListAsync();
    }
}
