using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Web.Features.Social;

namespace Shortnr.Tests.Unit.Services;

/// <summary>
/// Unit tests for SocialCache: in-memory caching, DB fallback, TTL expiry, and invalidation.
/// </summary>
public class SocialCacheTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly SocialCache _cache;

    public SocialCacheTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);

        var services = new ServiceCollection();
        services.AddSingleton(_db);
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var optionsMonitor = Options.Create(new SocialCacheOptions { CacheTtlMinutes = 15 });
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger<SocialCache>();

        _cache = new SocialCache(scopeFactory, optionsMonitor, logger);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task GetAsync_Miss_ReturnsNull()
    {
        var result = await _cache.GetAsync(999);
        Assert.Null(result);
    }

    [Fact]
    public async Task SetAndGet_ReturnsCachedData()
    {
        var data = new SocialData
        {
            Posts = [],
            AudienceCount = 1000,
            DisplayName = "Test User"
        };

        _cache.Set(1, data);
        var result = await _cache.GetAsync(1);

        Assert.NotNull(result);
        Assert.Equal(1000, result.AudienceCount);
        Assert.Equal("Test User", result.DisplayName);
    }

    [Fact]
    public async Task SetAndGet_WithPosts_ReturnsPosts()
    {
        var data = new SocialData
        {
            Posts = new List<SocialPostItem>
            {
                new() { ExternalPostId = "p1", Title = "Post 1", Text = "Hello" },
                new() { ExternalPostId = "p2", Title = "Post 2", Text = "World" }
            },
            AudienceCount = 500
        };

        _cache.Set(2, data);
        var result = await _cache.GetAsync(2);

        Assert.NotNull(result);
        Assert.Equal(2, result.Posts.Count);
        Assert.Equal("Post 1", result.Posts[0].Title);
    }

    [Fact]
    public async Task Invalidate_RemovesCachedEntry()
    {
        _cache.Set(3, new SocialData { Posts = [], AudienceCount = 300 });
        Assert.NotNull(await _cache.GetAsync(3));

        _cache.Invalidate(3);
        Assert.Null(await _cache.GetAsync(3));
    }

    [Fact]
    public async Task Invalidate_NonExistent_DoesNotThrow()
    {
        _cache.Invalidate(99999);
        // No exception thrown
    }

    [Fact]
    public async Task Set_OverwritesExisting()
    {
        _cache.Set(4, new SocialData { Posts = [], AudienceCount = 100 });
        _cache.Set(4, new SocialData { Posts = [], AudienceCount = 200 });

        var result = await _cache.GetAsync(4);
        Assert.Equal(200, result!.AudienceCount);
    }

    [Fact]
    public async Task MultipleAccounts_Independent()
    {
        _cache.Set(10, new SocialData { Posts = [], AudienceCount = 100 });
        _cache.Set(20, new SocialData { Posts = [], AudienceCount = 200 });

        var a = await _cache.GetAsync(10);
        var b = await _cache.GetAsync(20);

        Assert.Equal(100, a!.AudienceCount);
        Assert.Equal(200, b!.AudienceCount);
    }

    [Fact]
    public async Task DBFallback_LoadsCachedPosts()
    {
        // Seed DB with social account and posts
        var account = new SocialAccount
        {
            Provider = SocialProvider.Twitter,
            OwnerUserId = 1,
            ExternalId = "tw-123",
            Username = "dbuser",
            FollowerCount = 750,
            IsLinked = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        _db.SocialAccounts.Add(account);
        await _db.SaveChangesAsync();

        _db.SocialPosts.Add(new SocialPost
        {
            SocialAccountId = account.Id,
            ExternalPostId = "post-1",
            Title = "DB Post",
            Text = "From database",
            FetchedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        // Cache miss should fall back to DB
        var result = await _cache.GetAsync(account.Id);
        Assert.NotNull(result);
        Assert.Equal(750, result.AudienceCount);
        Assert.Single(result.Posts);
        Assert.Equal("DB Post", result.Posts[0].Title);
    }

    [Fact]
    public async Task DBFallback_NoPosts_ReturnsNull()
    {
        var account = new SocialAccount
        {
            Provider = SocialProvider.YouTube,
            OwnerUserId = 1,
            ExternalId = "yt-456",
            Username = "emptychannel",
            IsLinked = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        _db.SocialAccounts.Add(account);
        await _db.SaveChangesAsync();

        var result = await _cache.GetAsync(account.Id);
        Assert.Null(result);
    }
}
