using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shortnr.Data;
using Shortnr.Data.Entities;

namespace Shortnr.Web.Features.Social;

public interface ISocialCache
{
    Task<SocialData?> GetAsync(long socialAccountId, CancellationToken ct = default);
    void Set(long socialAccountId, SocialData data);
    void Invalidate(long socialAccountId);
}

public class SocialCacheOptions
{
    public int CacheTtlMinutes { get; set; } = 15;
}

/// <summary>
/// In-memory cache for social platform data with configurable TTL (PRD-021).
/// Falls back to the database-cached SocialPost rows when the in-memory
/// cache is cold, ensuring the bio page always has data to show even
/// before the first background refresh completes.
/// </summary>
public class SocialCache : ISocialCache
{
    private readonly ConcurrentDictionary<long, CacheEntry> _cache = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<SocialCacheOptions> _options;
    private readonly ILogger<SocialCache> _logger;

    public SocialCache(
        IServiceScopeFactory scopeFactory,
        IOptions<SocialCacheOptions> options,
        ILogger<SocialCache> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    public Task<SocialData?> GetAsync(long socialAccountId, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(socialAccountId, out var entry) && !entry.IsExpired(_options.Value.CacheTtlMinutes))
        {
            return Task.FromResult<SocialData?>(entry.Data);
        }

        return LoadFromDatabaseAsync(socialAccountId, ct);
    }

    public void Set(long socialAccountId, SocialData data)
    {
        _cache[socialAccountId] = new CacheEntry(data, DateTime.UtcNow);
        _logger.LogDebug("Cached social data for account {AccountId}", socialAccountId);
    }

    public void Invalidate(long socialAccountId)
    {
        _cache.TryRemove(socialAccountId, out _);
        _logger.LogDebug("Invalidated cache for account {AccountId}", socialAccountId);
    }

    private async Task<SocialData?> LoadFromDatabaseAsync(long socialAccountId, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var posts = await db.SocialPosts
                .AsNoTracking()
                .Where(p => p.SocialAccountId == socialAccountId)
                .OrderByDescending(p => p.PublishedAtUtc)
                .Take(3)
                .ToListAsync(ct);

            if (posts.Count == 0)
                return null;

            var account = await db.SocialAccounts
                .AsNoTracking()
                .Where(a => a.Id == socialAccountId)
                .Select(a => new { a.FollowerCount, a.SubscriberCount, a.DisplayName, a.AvatarUrl })
                .FirstOrDefaultAsync(ct);

            if (account is null)
                return null;

            var data = new SocialData
            {
                Posts = posts.Select(p => new SocialPostItem
                {
                    ExternalPostId = p.ExternalPostId,
                    Title = p.Title,
                    Text = p.Text,
                    MediaUrl = p.MediaUrl,
                    Permalink = p.Permalink,
                    PublishedAtUtc = p.PublishedAtUtc
                }).ToList(),
                AudienceCount = account.SubscriberCount ?? account.FollowerCount,
                DisplayName = account.DisplayName,
                AvatarUrl = account.AvatarUrl
            };

            Set(socialAccountId, data);
            return data;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load social data from database for account {AccountId}", socialAccountId);
            return null;
        }
    }

    private sealed class CacheEntry(SocialData data, DateTime createdAt)
    {
        public SocialData Data { get; } = data;
        public DateTime CreatedAt { get; } = createdAt;

        public bool IsExpired(int ttlMinutes) =>
            DateTime.UtcNow - CreatedAt > TimeSpan.FromMinutes(ttlMinutes);
    }
}
