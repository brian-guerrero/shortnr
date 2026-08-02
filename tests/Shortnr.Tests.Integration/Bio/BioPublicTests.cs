using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Tests.Integration.Infrastructure;

namespace Shortnr.Tests.Integration.Bio;

/// <summary>
/// Verifies the public /bio/{slug} page: rendering, 404s, theme application,
/// no-auth access, and that rendered links route through the real redirect.
/// </summary>
public class BioPublicTests : IAsyncLifetime
{
    private readonly ShortnrWebAppFactory _factory = new(authEnabled: true);

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    [Fact]
    public async Task PublicPage_RendersVisibleLinks()
    {
        var ownerId = await SeedUserAsync("alice");
        var pageId = await SeedBioPageAsync(ownerId, "alicebio", "Alice Corner", "forest");
        var visibleLinkId = await SeedLinkAsync(ownerId, "abc123", "https://example.com/one");
        var hiddenLinkId = await SeedLinkAsync(ownerId, "xyz999", "https://example.com/hidden");
        await SeedBioPageLinkAsync(pageId, visibleLinkId, "My Remix", 0, true);
        await SeedBioPageLinkAsync(pageId, hiddenLinkId, "Secret", 1, false);

        var response = await _factory.CreateClient().GetAsync("/bio/alicebio");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Alice Corner", body);
        Assert.Contains("My Remix", body);
        Assert.Contains("href=\"/abc123\"", body);
        Assert.DoesNotContain("Secret", body);
        Assert.DoesNotContain("xyz999", body);
    }

    [Fact]
    public async Task PublicPage_UnknownSlug_Returns404()
    {
        var response = await _factory.CreateClient().GetAsync("/bio/nope");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("doesn't exist", body);
    }

    [Fact]
    public async Task PublicPage_AllLinksHidden_Returns404()
    {
        var ownerId = await SeedUserAsync("alice");
        var pageId = await SeedBioPageAsync(ownerId, "alicebio", "Alice", "default");
        var linkId = await SeedLinkAsync(ownerId, "abc123", "https://example.com/one");
        await SeedBioPageLinkAsync(pageId, linkId, "Hidden", 0, false);

        var response = await _factory.CreateClient().GetAsync("/bio/alicebio");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PublicPage_RequiresNoAuth()
    {
        var ownerId = await SeedUserAsync("alice");
        var pageId = await SeedBioPageAsync(ownerId, "alicebio", "Alice", "default");
        var linkId = await SeedLinkAsync(ownerId, "abc123", "https://example.com/one");
        await SeedBioPageLinkAsync(pageId, linkId, "Link", 0, true);

        var response = await _factory.CreateClient().GetAsync("/bio/alicebio");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PublicPage_AppliesTheme()
    {
        var ownerId = await SeedUserAsync("alice");
        var pageId = await SeedBioPageAsync(ownerId, "alicebio", "Alice", "ocean");
        var linkId = await SeedLinkAsync(ownerId, "abc123", "https://example.com/one");
        await SeedBioPageLinkAsync(pageId, linkId, "Link", 0, true);

        var body = await _factory.CreateClient().GetStringAsync("/bio/alicebio");

        Assert.Contains("data-bio-theme=\"ocean\"", body);
    }

    [Fact]
    public async Task PublicPage_LinkRoutesThroughRedirect()
    {
        var ownerId = await SeedUserAsync("alice");
        var pageId = await SeedBioPageAsync(ownerId, "alicebio", "Alice", "default");
        var linkId = await SeedLinkAsync(ownerId, "abc123", "https://example.com/one");
        await SeedBioPageLinkAsync(pageId, linkId, "My Remix", 0, true);

        using var client = _factory.CreateClientNoRedirect();
        var page = await client.GetAsync("/bio/alicebio");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var body = await page.Content.ReadAsStringAsync();
        Assert.Contains("href=\"/abc123\"", body);

        var redirect = await client.GetAsync("/abc123");
        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        Assert.Equal("https://example.com/one", redirect.Headers.Location?.ToString());
    }

    private async Task<long> SeedUserAsync(string subject)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new User
        {
            Issuer = ShortnrWebAppFactory.TestIssuer,
            Subject = subject,
            CreatedAtUtc = DateTime.UtcNow,
            LastLoginAtUtc = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private async Task<long> SeedBioPageAsync(long ownerUserId, string slug, string displayName, string theme)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var page = new BioPage
        {
            OwnerUserId = ownerUserId,
            Slug = slug,
            DisplayName = displayName,
            Theme = theme,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.BioPages.Add(page);
        await db.SaveChangesAsync();
        return page.Id;
    }

    private async Task<long> SeedLinkAsync(long ownerUserId, string shortCode, string longUrl)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var link = new ShortenedUrl
        {
            OwnerUserId = ownerUserId,
            ShortCode = shortCode,
            LongUrl = longUrl,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.ShortenedUrls.Add(link);
        await db.SaveChangesAsync();
        return link.Id;
    }

    private async Task SeedBioPageLinkAsync(long bioPageId, long shortUrlId, string title, int sortOrder, bool isVisible)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.BioPageLinks.Add(new BioPageLink
        {
            BioPageId = bioPageId,
            ShortenedUrlId = shortUrlId,
            Title = title,
            SortOrder = sortOrder,
            IsVisible = isVisible
        });
        await db.SaveChangesAsync();
    }
}
