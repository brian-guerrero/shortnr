using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Web.Features.Social;
using Shortnr.Web.Features.ShortLinks;

namespace Shortnr.Web.Pages.Bio;

/// <summary>
/// Bio sub-link page that serves OG/Twitter Card meta tags for rich unfurling (PRD-021).
/// Social crawlers see the meta tags; regular users are redirected to the destination.
/// </summary>
public partial class SubLinkModel : PageModel
{
    private static readonly Regex SocialCrawlerPattern = new(
        """(facebookexternalhit|Twitterbot|LinkedInBot|WhatsApp|Slackbot|Discordbot|Googlebot|Pinterest|TelegramBot)""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly AppDbContext _db;
    private readonly OgFetcherService _ogFetcher;

    public BioPage? BioPage { get; private set; }
    public long LinkId { get; private set; }
    public string? LinkTitle { get; private set; }
    public string DestinationUrl { get; private set; } = "/";
    public OgMetadata? Og { get; private set; }
    public bool IsSocialCrawler { get; private set; }

    public SubLinkModel(AppDbContext db, OgFetcherService ogFetcher)
    {
        _db = db;
        _ogFetcher = ogFetcher;
    }

    public async Task<IActionResult> OnGet(string slug, long linkId)
    {
        var bioPage = await _db.BioPages
            .AsNoTracking()
            .Include(b => b.Links)
            .ThenInclude(l => l.ShortenedUrl)
            .ThenInclude(s => s!.Domain)
            .FirstOrDefaultAsync(b => b.Slug == slug);

        if (bioPage is null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return Page();
        }

        var link = bioPage.Links.FirstOrDefault(l => l.Id == linkId && l.IsVisible);
        if (link?.ShortenedUrl is null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return Page();
        }

        BioPage = bioPage;
        LinkId = linkId;
        LinkTitle = link.Title;
        DestinationUrl = ShortUrlHelper.DisplayHref(
            Request.Scheme,
            link.ShortenedUrl.Domain?.Hostname,
            link.ShortenedUrl.ShortCode);

        // Detect social crawlers by user agent
        var userAgent = Request.Headers.UserAgent.ToString();
        IsSocialCrawler = SocialCrawlerPattern.IsMatch(userAgent);

        if (IsSocialCrawler)
        {
            // Fetch OG metadata for crawlers
            Og = await _ogFetcher.GetOgMetadataAsync(link.ShortenedUrlId, link.ShortenedUrl.LongUrl);
        }

        return Page();
    }
}
