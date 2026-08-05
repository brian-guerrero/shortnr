using System.Net;
using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Tests.Integration.Infrastructure;

namespace Shortnr.Tests.Integration.Bio;

public class BioSocialMetaTests : IAsyncLifetime
{
    private readonly ShortnrWebAppFactory _factory = new(authEnabled: true);

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    [Fact]
    public async Task PublicPage_RendersOgMetaTags()
    {
        var ownerId = await SeedUserAsync("alice");
        var pageId = await SeedBioPageAsync(ownerId, "alicebio", "Alice Corner", "forest",
            bioText: "Creator, builder, dreamer.", avatarUrl: "https://cdn.example.com/alice.png");
        var linkId = await SeedLinkAsync(ownerId, "abc123", "https://example.com/one");
        await SeedBioPageLinkAsync(pageId, linkId, "My Site", 0, true);

        var body = await _factory.CreateClient().GetStringAsync("/bio/alicebio");

        Assert.Contains("property=\"og:title\" content=\"Alice Corner\"", body);
        Assert.Contains("property=\"og:description\" content=\"Creator, builder, dreamer.\"", body);
        Assert.Contains("property=\"og:image\" content=\"https://cdn.example.com/alice.png\"", body);
        Assert.Contains("property=\"og:url\"", body);
        Assert.Contains("property=\"og:type\" content=\"profile\"", body);
    }

    [Fact]
    public async Task PublicPage_RendersTwitterCardTags()
    {
        var ownerId = await SeedUserAsync("alice");
        var pageId = await SeedBioPageAsync(ownerId, "alicebio", "Alice", "default",
            avatarUrl: "https://cdn.example.com/alice.png");
        var linkId = await SeedLinkAsync(ownerId, "abc123", "https://example.com/one");
        await SeedBioPageLinkAsync(pageId, linkId, "Link", 0, true);

        var body = await _factory.CreateClient().GetStringAsync("/bio/alicebio");

        Assert.Contains("name=\"twitter:card\" content=\"summary\"", body);
        Assert.Contains("name=\"twitter:title\" content=\"Alice\"", body);
        Assert.Contains("name=\"twitter:image\" content=\"https://cdn.example.com/alice.png\"", body);
    }

    [Fact]
    public async Task PublicPage_NoAvatar_UsesPlaceholderImageAndLargeCard()
    {
        var ownerId = await SeedUserAsync("alice");
        var pageId = await SeedBioPageAsync(ownerId, "alicebio", "Alice", "default");
        var linkId = await SeedLinkAsync(ownerId, "abc123", "https://example.com/one");
        await SeedBioPageLinkAsync(pageId, linkId, "Link", 0, true);

        var body = await _factory.CreateClient().GetStringAsync("/bio/alicebio");

        Assert.Contains("name=\"twitter:card\" content=\"summary_large_image\"", body);
        Assert.Contains("/img/bio-og-default.svg", body);
        Assert.Contains("property=\"og:image\"", body);
    }

    [Fact]
    public async Task PublicPage_LongBioText_TruncatesDescription()
    {
        var longText = new string('A', 200);
        var ownerId = await SeedUserAsync("alice");
        var pageId = await SeedBioPageAsync(ownerId, "alicebio", "Alice", "default", bioText: longText);
        var linkId = await SeedLinkAsync(ownerId, "abc123", "https://example.com/one");
        await SeedBioPageLinkAsync(pageId, linkId, "Link", 0, true);

        var body = await _factory.CreateClient().GetStringAsync("/bio/alicebio");

        var truncated = new string('A', 160) + "&#x2026;";
        Assert.Contains($"content=\"{truncated}\"", body);
    }

    [Fact]
    public async Task PublicPage_NoBioText_UsesFallbackDescription()
    {
        var ownerId = await SeedUserAsync("alice");
        var pageId = await SeedBioPageAsync(ownerId, "alicebio", "Alice Corner", "default");
        var linkId = await SeedLinkAsync(ownerId, "abc123", "https://example.com/one");
        await SeedBioPageLinkAsync(pageId, linkId, "Link", 0, true);

        var body = await _factory.CreateClient().GetStringAsync("/bio/alicebio");

        Assert.Contains("Alice Corner&#x27;s bio page on shortnr", body);
    }

    [Fact]
    public async Task PublicPage_OgUrl_UsesRequestHost()
    {
        var ownerId = await SeedUserAsync("alice");
        var pageId = await SeedBioPageAsync(ownerId, "alicebio", "Alice", "default");
        var linkId = await SeedLinkAsync(ownerId, "abc123", "https://example.com/one");
        await SeedBioPageLinkAsync(pageId, linkId, "Link", 0, true);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Host = "go.example.com";
        var body = await client.GetStringAsync("/bio/alicebio");

        Assert.Contains("property=\"og:url\" content=\"http://go.example.com/bio/alicebio\"", body);
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

    private async Task<long> SeedBioPageAsync(long ownerUserId, string slug, string displayName, string theme,
        string? bioText = null, string? avatarUrl = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var page = new BioPage
        {
            OwnerUserId = ownerUserId,
            Slug = slug,
            DisplayName = displayName,
            Theme = theme,
            BioText = bioText,
            AvatarUrl = avatarUrl,
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
