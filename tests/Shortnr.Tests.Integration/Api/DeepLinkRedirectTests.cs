using System.Net;
using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Tests.Integration.Infrastructure;

namespace Shortnr.Tests.Integration.Api;

/// <summary>
/// Verifies platform-aware deep linking on the redirect endpoint: iOS and Android
/// user agents are routed to their platform-specific target when configured,
/// everyone else falls back to the original URL.
/// </summary>
public class DeepLinkRedirectTests : IAsyncLifetime
{
    private const string IosUa =
        "Mozilla/5.0 (iPhone; CPU iPhone OS 17_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Mobile/15E148 Safari/604.1";
    private const string AndroidUa =
        "Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Mobile Safari/537.36";
    private const string DesktopUa =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36";

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
    public async Task Redirect_IosUa_SendsIosDeepLink()
    {
        await SeedAsync("app", "https://example.com/app", "myapp://open/123", "https://play.google.com/store/apps/details?id=com.example.app");

        var response = await GetAsync("/app", IosUa);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("myapp://open/123", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Redirect_AndroidUa_SendsAndroidDeepLink()
    {
        await SeedAsync("app2", "https://example.com/app2", "myapp://open/123", "https://play.google.com/store/apps/details?id=com.example.app");

        var response = await GetAsync("/app2", AndroidUa);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("https://play.google.com/store/apps/details?id=com.example.app", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Redirect_DesktopUa_FallsBackToOriginalUrl()
    {
        await SeedAsync("app3", "https://example.com/app3", "myapp://open/123", "https://play.google.com/store/apps/details?id=com.example.app");

        var response = await GetAsync("/app3", DesktopUa);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("https://example.com/app3", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Redirect_IosUaWithoutIosLink_FallsBackToOriginalUrl()
    {
        await SeedAsync("app4", "https://example.com/app4", null, "https://play.google.com/store/apps/details?id=com.example.app");

        var response = await GetAsync("/app4", IosUa);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("https://example.com/app4", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Redirect_PixelAndIosDeepLink_ServesInterstitialTargetingIosLink()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.ShortenedUrls.Add(new ShortenedUrl
        {
            LongUrl = "https://example.com/hybrid",
            ShortCode = "hybrid",
            CreatedAtUtc = DateTime.UtcNow,
            Metadata = new ShortenedUrlMetadata
            {
                PixelSnippetId = 1,
                PixelId = "555",
                IosDeepLink = "myapp://open/999",
                AndroidDeepLink = null
            }
        });
        await db.SaveChangesAsync();

        var response = await GetAsync("/hybrid", IosUa);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("fbq('init', '555')", body);
        Assert.Contains("myapp://open/999", body);
        Assert.Contains("window.location.replace", body);
    }

    private async Task SeedAsync(string code, string longUrl, string? ios, string? android)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.ShortenedUrls.Add(new ShortenedUrl
        {
            LongUrl = longUrl,
            ShortCode = code,
            CreatedAtUtc = DateTime.UtcNow,
            Metadata = new ShortenedUrlMetadata { IosDeepLink = ios, AndroidDeepLink = android }
        });
        await db.SaveChangesAsync();
    }

    private async Task<HttpResponseMessage> GetAsync(string path, string userAgent)
    {
        var client = _factory.CreateClientNoRedirect();
        client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        return await client.GetAsync(path);
    }
}
