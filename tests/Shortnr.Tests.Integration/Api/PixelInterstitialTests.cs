using System.Net;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Tests.Integration.Infrastructure;

namespace Shortnr.Tests.Integration.Api;

/// <summary>
/// Verifies the retargeting-pixel redirect branch: links with a pixel attached
/// serve a lightweight interstitial HTML page that embeds the pixel snippet and
/// JS-redirects to the destination, while links without a pixel keep the direct
/// 302 fast path unchanged.
/// </summary>
public class PixelInterstitialTests : IAsyncLifetime
{
    private readonly ShortnrWebAppFactory _factory = new(authEnabled: false);

    public Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.ClickEvents.RemoveRange(db.ClickEvents);
        db.ShortenedUrls.RemoveRange(db.ShortenedUrls);
        db.Domains.RemoveRange(db.Domains);
        db.Users.RemoveRange(db.Users);
        return db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    [Fact]
    public async Task Redirect_NoPixel_IsDirect302()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.ShortenedUrls.Add(new ShortenedUrl
        {
            LongUrl = "https://example.com/plain",
            ShortCode = "plain",
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var response = await _factory.CreateClientNoRedirect().GetAsync("/plain");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("https://example.com/plain", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Redirect_MetaPixelTemplate_ServesInterstitialWithPixelAndJsRedirect()
    {
        await SeedPixelLinkAsync("meta", 1, "555123456");

        var response = await _factory.CreateClientNoRedirect().GetAsync("/meta");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("fbq('init', '555123456')", body);
        Assert.Contains("window.location.replace(\"https://example.com/meta-dest\");", body);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
    }

    [Fact]
    public async Task Redirect_GoogleAdsTemplate_ServesInterstitialWithPixel()
    {
        await SeedPixelLinkAsync("google", 2, "AW-111222333");

        var response = await _factory.CreateClientNoRedirect().GetAsync("/google");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("gtag('config', 'AW-111222333')", body);
        Assert.Contains("https://www.googletagmanager.com/gtag/js?id=AW-111222333", body);
    }

    [Fact]
    public async Task Redirect_CustomSnippet_ServesRawHtmlVerbatim()
    {
        const string rawSnippet = "<script>console.log('custom pixel');</script>";
        await SeedPixelLinkAsync("custom", 3, rawSnippet);

        var response = await _factory.CreateClientNoRedirect().GetAsync("/custom");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(rawSnippet, body);
    }

    [Fact]
    public async Task Redirect_TemplateWithoutPixelId_FallsBackTo302()
    {
        await SeedPixelLinkAsync("no-id", 1, null);

        var response = await _factory.CreateClientNoRedirect().GetAsync("/no-id");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("https://example.com/no-id-dest", response.Headers.Location?.ToString());
    }

    private async Task SeedPixelLinkAsync(string shortCode, long pixelSnippetId, string? pixelId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.ShortenedUrls.Add(new ShortenedUrl
        {
            LongUrl = $"https://example.com/{shortCode}-dest",
            ShortCode = shortCode,
            CreatedAtUtc = DateTime.UtcNow,
            Metadata = new ShortenedUrlMetadata
            {
                PixelSnippetId = pixelSnippetId,
                PixelId = pixelId
            }
        });
        await db.SaveChangesAsync();
    }
}
