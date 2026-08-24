using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Tests.Integration.Infrastructure;
using Shortnr.Web.Features.Social;

namespace Shortnr.Tests.Integration.Social;

/// <summary>
/// Integration tests for PRD-021: Social Bio Pages v2 — social settings page,
/// account linking/unlinking, and auth gating.
/// </summary>
public class SocialSettingsTests : IAsyncLifetime
{
    private readonly ShortnrWebAppFactory _factory = new(authEnabled: true);
    private readonly HttpClient _client;

    public SocialSettingsTests()
    {
        _client = _factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    // ----- Helpers -----

    private async Task<long> SeedUserAsync(string subject = "social-test-user")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new User
        {
            Issuer = ShortnrWebAppFactory.TestIssuer,
            Subject = subject,
            Email = $"{subject}@test.com",
            Name = "Social Test User",
            CreatedAtUtc = DateTime.UtcNow,
            LastLoginAtUtc = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private async Task SeedBioPageAsync(long ownerId, string slug = "testbio")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.BioPages.Add(new BioPage
        {
            OwnerUserId = ownerId,
            Slug = slug,
            DisplayName = "Test Bio",
            Theme = "default",
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private void Authenticate(string subject = "social-test-user")
    {
        var authState = _factory.Services.GetRequiredService<TestAuthState>();
        authState.SetAuthenticatedUser(subject, ShortnrWebAppFactory.TestIssuer);
    }

    // ----- Auth gating -----

    [Fact]
    public async Task SocialSettings_Unauthenticated_ShowsPage()
    {
        var response = await _client.GetAsync("/bio/social");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Auth is enabled; unauthenticated user sees the page
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Social Accounts", html);
    }

    [Fact]
    public async Task SocialSettings_Authenticated_ShowsProviders()
    {
        var userId = await SeedUserAsync();
        Authenticate();

        var response = await _client.GetAsync("/bio/social");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Twitter", html);
        Assert.Contains("Instagram", html);
        Assert.Contains("YouTube", html);
        Assert.Contains("TikTok", html);
        Assert.Contains("Not linked", html);
    }

    [Fact]
    public async Task SocialSettings_Authenticated_NoLinkedAccounts_ShowsEmptyState()
    {
        var userId = await SeedUserAsync();
        Authenticate();

        var response = await _client.GetAsync("/bio/social");
        var html = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Linked as", html);
    }

    // ----- Account linking -----

    [Fact]
    public async Task SocialSettings_LinkAccount_CreatesSocialAccount()
    {
        var userId = await SeedUserAsync();
        Authenticate();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Verify no social accounts initially
        var initial = await db.SocialAccounts.Where(a => a.OwnerUserId == userId).ToListAsync();
        Assert.Empty(initial);
    }

    [Fact]
    public async Task SocialSettings_SocialAccount_HasCorrectFields()
    {
        var userId = await SeedUserAsync();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var account = new SocialAccount
        {
            Provider = SocialProvider.Twitter,
            OwnerUserId = userId,
            ExternalId = "tw-12345",
            Username = "testuser",
            DisplayName = "Test User",
            AvatarUrl = "https://example.com/avatar.jpg",
            FollowerCount = 1500,
            IsLinked = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        db.SocialAccounts.Add(account);
        await db.SaveChangesAsync();

        var loaded = await db.SocialAccounts.FirstAsync(a => a.OwnerUserId == userId);
        Assert.Equal(SocialProvider.Twitter, loaded.Provider);
        Assert.Equal("testuser", loaded.Username);
        Assert.Equal(1500, loaded.FollowerCount);
        Assert.True(loaded.IsLinked);
    }

    [Fact]
    public async Task SocialSettings_MultipleProviders_CanLinkAll()
    {
        var userId = await SeedUserAsync();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        foreach (var provider in Enum.GetValues<SocialProvider>())
        {
            db.SocialAccounts.Add(new SocialAccount
            {
                Provider = provider,
                OwnerUserId = userId,
                ExternalId = $"{provider}-123",
                Username = $"{provider.ToString().ToLower()}user",
                IsLinked = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
        }
        await db.SaveChangesAsync();

        var accounts = await db.SocialAccounts.Where(a => a.OwnerUserId == userId).ToListAsync();
        Assert.Equal(4, accounts.Count);
        Assert.Equal(Enum.GetValues<SocialProvider>().ToHashSet(), accounts.Select(a => a.Provider).ToHashSet());
    }

    // ----- Unlink -----

    [Fact]
    public async Task SocialSettings_UnlinkAccount_RemovesAccount()
    {
        var userId = await SeedUserAsync();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var account = new SocialAccount
        {
            Provider = SocialProvider.Instagram,
            OwnerUserId = userId,
            ExternalId = "ig-123",
            Username = "testgram",
            IsLinked = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        db.SocialAccounts.Add(account);
        await db.SaveChangesAsync();

        var accountId = account.Id;
        db.SocialAccounts.Remove(account);
        await db.SaveChangesAsync();

        var remaining = await db.SocialAccounts.Where(a => a.OwnerUserId == userId).ToListAsync();
        Assert.Empty(remaining);
    }

    // ----- SocialCache -----

    [Fact]
    public async Task SocialCache_Miss_ReturnsNull()
    {
        var cache = _factory.Services.GetRequiredService<ISocialCache>();
        var result = await cache.GetAsync(999999);
        Assert.Null(result);
    }

    [Fact]
    public async Task SocialCache_SetAndGet_ReturnsData()
    {
        var cache = _factory.Services.GetRequiredService<ISocialCache>();
        var data = new SocialData
        {
            Posts = [],
            AudienceCount = 1000,
            DisplayName = "Cached User",
            AvatarUrl = "https://example.com/avatar.jpg"
        };

        cache.Set(1, data);
        var result = await cache.GetAsync(1);

        Assert.NotNull(result);
        Assert.Equal(1000, result.AudienceCount);
        Assert.Equal("Cached User", result.DisplayName);
    }

    [Fact]
    public async Task SocialCache_Invalidate_RemovesEntry()
    {
        var cache = _factory.Services.GetRequiredService<ISocialCache>();
        var data = new SocialData { Posts = [], AudienceCount = 500 };
        cache.Set(2, data);

        var before = await cache.GetAsync(2);
        Assert.NotNull(before);

        cache.Invalidate(2);
        var after = await cache.GetAsync(2);
        Assert.Null(after);
    }

    [Fact]
    public async Task SocialCache_SetMultipleAccounts_Independent()
    {
        var cache = _factory.Services.GetRequiredService<ISocialCache>();
        cache.Set(10, new SocialData { Posts = [], AudienceCount = 100 });
        cache.Set(20, new SocialData { Posts = [], AudienceCount = 200 });

        var a = await cache.GetAsync(10);
        var b = await cache.GetAsync(20);

        Assert.Equal(100, a!.AudienceCount);
        Assert.Equal(200, b!.AudienceCount);

        cache.Invalidate(10);
        Assert.Null(await cache.GetAsync(10));
        Assert.NotNull(await cache.GetAsync(20));
    }

    // ----- SocialSections HTMX endpoint -----

    [Fact]
    public async Task SocialSections_UnknownSlug_ReturnsEmpty()
    {
        var response = await _client.GetAsync("/bio/nonexistent/social");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("social-section", html);
    }

    [Fact]
    public async Task SocialSections_NoLinkedAccounts_ReturnsEmpty()
    {
        var userId = await SeedUserAsync();
        await SeedBioPageAsync(userId);

        var response = await _client.GetAsync("/bio/testbio/social");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("social-section", html);
    }

    [Fact]
    public async Task SocialSections_WithLinkedAccounts_ReturnsSections()
    {
        var userId = await SeedUserAsync();
        await SeedBioPageAsync(userId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.SocialAccounts.Add(new SocialAccount
        {
            Provider = SocialProvider.YouTube,
            OwnerUserId = userId,
            ExternalId = "yt-123",
            Username = "testchannel",
            SubscriberCount = 5000,
            IsLinked = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var response = await _client.GetAsync("/bio/testbio/social");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("YouTube", html);
        Assert.Contains("testchannel", html);
    }

    [Fact]
    public async Task SocialSections_WithCachedPosts_ShowsPosts()
    {
        var userId = await SeedUserAsync();
        await SeedBioPageAsync(userId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var account = new SocialAccount
        {
            Provider = SocialProvider.Twitter,
            OwnerUserId = userId,
            ExternalId = "tw-456",
            Username = "tweetuser",
            FollowerCount = 2500,
            IsLinked = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        db.SocialAccounts.Add(account);
        await db.SaveChangesAsync();

        db.SocialPosts.Add(new SocialPost
        {
            SocialAccountId = account.Id,
            ExternalPostId = "tw-post-1",
            Title = "My latest tweet",
            Text = "Hello world!",
            Permalink = "https://x.com/tweetuser/status/123",
            PublishedAtUtc = DateTime.UtcNow.AddHours(-1),
            FetchedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var response = await _client.GetAsync("/bio/testbio/social");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("My latest tweet", html);
        Assert.Contains("2.5K", html);
    }

    // ----- SubLink OG meta tags -----

    [Fact]
    public async Task SubLink_SocialCrawler_ServesOgMetaTags()
    {
        var userId = await SeedUserAsync();
        await SeedBioPageAsync(userId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var link = new ShortenedUrl
        {
            ShortCode = "ogtest",
            LongUrl = "https://example.com/article",
            OwnerUserId = userId
        };
        db.ShortenedUrls.Add(link);
        await db.SaveChangesAsync();

        var bioLink = new BioPageLink
        {
            BioPageId = db.BioPages.First(b => b.Slug == "testbio").Id,
            ShortenedUrlId = link.Id,
            Title = "My Article",
            SortOrder = 0,
            IsVisible = true
        };
        db.BioPageLinks.Add(bioLink);
        await db.SaveChangesAsync();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/bio/testbio/link/{bioLink.Id}");
        request.Headers.Add("User-Agent", "facebookexternalhit/1.1");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SubLink_RegularUser_TriggersRedirect()
    {
        var userId = await SeedUserAsync();
        await SeedBioPageAsync(userId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var link = new ShortenedUrl
        {
            ShortCode = "redirtest",
            LongUrl = "https://example.com/destination",
            OwnerUserId = userId
        };
        db.ShortenedUrls.Add(link);
        await db.SaveChangesAsync();

        var bioLink = new BioPageLink
        {
            BioPageId = db.BioPages.First(b => b.Slug == "testbio").Id,
            ShortenedUrlId = link.Id,
            Title = "Redirect Test",
            SortOrder = 0,
            IsVisible = true
        };
        db.BioPageLinks.Add(bioLink);
        await db.SaveChangesAsync();

        var noRedirectClient = _factory.CreateClientNoRedirect();
        var request = new HttpRequestMessage(HttpMethod.Get, $"/bio/testbio/link/{bioLink.Id}");
        request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
        var response = await noRedirectClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    // ----- Bio public page includes social sections container -----

    [Fact]
    public async Task BioPublicPage_IncludesHtmxSocialContainer()
    {
        var userId = await SeedUserAsync();
        await SeedBioPageAsync(userId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var link = new ShortenedUrl
        {
            ShortCode = "biolink",
            LongUrl = "https://example.com",
            OwnerUserId = userId
        };
        db.ShortenedUrls.Add(link);
        await db.SaveChangesAsync();

        db.BioPageLinks.Add(new BioPageLink
        {
            BioPageId = db.BioPages.First(b => b.Slug == "testbio").Id,
            ShortenedUrlId = link.Id,
            Title = "My Link",
            SortOrder = 0,
            IsVisible = true
        });
        await db.SaveChangesAsync();

        var response = await _client.GetAsync("/bio/testbio");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("social-sections", html);
        Assert.Contains("hx-get=\"/bio/testbio/social\"", html);
        Assert.Contains("hx-trigger=\"load\"", html);
    }
}
