using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;

namespace Shortnr.Web.Features.Social;

/// <summary>
/// Fetches and caches Open Graph metadata from destination URLs (PRD-021).
/// Bio sub-links that are shared directly unfurl with their own title/description/image
/// instead of a generic redirect card. The triple is refetched when the cache window expires.
/// </summary>
public class OgFetcherService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OgFetcherService> _logger;
    private readonly int _cacheHours;

    public OgFetcherService(
        IHttpClientFactory httpClientFactory,
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<OgFetcherService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _cacheHours = config.GetValue("Social:UnfurlCacheHours", 24);
    }

    /// <summary>
    /// Returns cached OG metadata for a short link, fetching from the destination
    /// URL if not cached or stale.
    /// </summary>
    public async Task<OgMetadata?> GetOgMetadataAsync(long shortenedUrlId, string destinationUrl, CancellationToken ct = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var metadata = await db.ShortenedUrlMetadatas
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.ShortenedUrlId == shortenedUrlId, ct);

            // Return cached if still fresh
            if (metadata?.OgTitle is not null &&
                metadata.OgFetchedAtUtc.HasValue &&
                metadata.OgFetchedAtUtc.Value > DateTime.UtcNow.AddHours(-_cacheHours))
            {
                return new OgMetadata
                {
                    Title = metadata.OgTitle,
                    Description = metadata.OgDescription,
                    Image = metadata.OgImage
                };
            }

            // Fetch fresh
            var fetched = await FetchOgFromUrlAsync(destinationUrl, ct);
            if (fetched is null) return null;

            // Upsert metadata
            if (metadata is not null)
            {
                metadata.OgTitle = fetched.Title;
                metadata.OgDescription = fetched.Description;
                metadata.OgImage = fetched.Image;
                metadata.OgFetchedAtUtc = DateTime.UtcNow;
            }
            else
            {
                db.ShortenedUrlMetadatas.Add(new ShortenedUrlMetadata
                {
                    ShortenedUrlId = shortenedUrlId,
                    OgTitle = fetched.Title,
                    OgDescription = fetched.Description,
                    OgImage = fetched.Image,
                    OgFetchedAtUtc = DateTime.UtcNow
                });
            }

            await db.SaveChangesAsync(ct);
            return fetched;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch OG metadata for URL {Url}", destinationUrl);
            return null;
        }
    }

    private async Task<OgMetadata?> FetchOgFromUrlAsync(string url, CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("SocialPlatforms");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "shortnr-bot/1.0 (social unfurling)");

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) return null;

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!contentType.Contains("html", StringComparison.OrdinalIgnoreCase))
                return null;

            // Read only the first 64KB to find <head> content
            var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, false, 8192);
            var buffer = new char[65536];
            var read = await reader.ReadAsync(buffer, ct);
            var html = new string(buffer, 0, read);

            return ParseOgTags(html);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to fetch OG from {Url}", url);
            return null;
        }
    }

    private static OgMetadata? ParseOgTags(string html)
    {
        var title = ExtractOgContent(html, "og:title");
        var description = ExtractOgContent(html, "og:description");
        var image = ExtractOgContent(html, "og:image");

        if (title is null && description is null && image is null)
            return null;

        return new OgMetadata
        {
            Title = title,
            Description = description?.Length > 2000 ? description[..2000] : description,
            Image = image
        };
    }

    private static string? ExtractOgContent(string html, string property)
    {
        var pattern = $"""<meta\s+(?:[^>]*?\s+)?property=["']({Regex.Escape(property)})["']\s+content=["']([^"']*)["']""";
        var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[2].Value.Trim() : null;
    }
}

public sealed class OgMetadata
{
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? Image { get; init; }
}
