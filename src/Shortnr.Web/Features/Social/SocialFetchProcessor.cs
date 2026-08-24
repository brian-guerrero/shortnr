using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;

namespace Shortnr.Web.Features.Social;

/// <summary>
/// Background processor that refreshes social account data on a schedule (PRD-021).
/// Mirrors the Channel<T>+BackgroundService pattern used by ClickBatchProcessor,
/// AiActivityProcessor, and UserProvisioningProcessor. Keeps all platform API
/// calls off the request path so slow/blocked providers never delay page loads.
/// </summary>
public class SocialFetchProcessor : BackgroundService
{
    private readonly Channel<SocialFetchRequest> _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SocialFetchProcessor> _logger;
    private readonly IEnumerable<ISocialPlatformProvider> _providers;
    private readonly ISocialCache _cache;

    public SocialFetchProcessor(
        Channel<SocialFetchRequest> channel,
        IServiceScopeFactory scopeFactory,
        ILogger<SocialFetchProcessor> logger,
        IEnumerable<ISocialPlatformProvider> providers,
        ISocialCache cache)
    {
        _channel = channel;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _providers = providers;
        _cache = cache;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SocialFetchProcessor starting");

        try
        {
            await foreach (var request in _channel.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await ProcessFetchAsync(request, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing social fetch for account {AccountId}", request.SocialAccountId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when stoppingToken is cancelled
        }

        _logger.LogInformation("SocialFetchProcessor stopping");
    }

    private async Task ProcessFetchAsync(SocialFetchRequest request, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var account = await db.SocialAccounts
            .FirstOrDefaultAsync(a => a.Id == request.SocialAccountId && a.IsLinked, ct);

        if (account is null)
        {
            _logger.LogDebug("Social account {AccountId} not found or not linked, skipping", request.SocialAccountId);
            return;
        }

        var provider = _providers.FirstOrDefault(p => p.Provider == account.Provider);
        if (provider is null)
        {
            _logger.LogWarning("No provider registered for {Provider}", account.Provider);
            return;
        }

        var data = await provider.FetchDataAsync(account, ct);

        if (data is null)
        {
            account.LastError = "Failed to fetch data from platform";
            account.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            _logger.LogWarning("Social fetch returned null for account {AccountId}", account.Id);
            return;
        }

        // Update account metadata
        account.FollowerCount = data.AudienceCount;
        account.DisplayName = data.DisplayName ?? account.DisplayName;
        account.AvatarUrl = data.AvatarUrl ?? account.AvatarUrl;
        account.LastSuccessUtc = DateTime.UtcNow;
        account.LastError = null;
        account.UpdatedAtUtc = DateTime.UtcNow;

        // Upsert posts
        var existingPostIds = await db.SocialPosts
            .Where(p => p.SocialAccountId == account.Id)
            .Select(p => p.ExternalPostId)
            .ToHashSetAsync(ct);

        var now = DateTime.UtcNow;
        foreach (var post in data.Posts)
        {
            if (existingPostIds.Contains(post.ExternalPostId))
            {
                // Update existing post
                var existing = await db.SocialPosts
                    .FirstOrDefaultAsync(p => p.SocialAccountId == account.Id && p.ExternalPostId == post.ExternalPostId, ct);

                if (existing is not null)
                {
                    existing.Title = post.Title;
                    existing.Text = post.Text;
                    existing.MediaUrl = post.MediaUrl;
                    existing.Permalink = post.Permalink;
                    existing.PublishedAtUtc = post.PublishedAtUtc;
                    existing.FetchedAtUtc = now;
                }
            }
            else
            {
                db.SocialPosts.Add(new SocialPost
                {
                    SocialAccountId = account.Id,
                    ExternalPostId = post.ExternalPostId,
                    Title = post.Title,
                    Text = post.Text,
                    MediaUrl = post.MediaUrl,
                    Permalink = post.Permalink,
                    PublishedAtUtc = post.PublishedAtUtc,
                    FetchedAtUtc = now
                });
            }
        }

        // Remove stale posts (keep only the latest 3)
        var keepIds = data.Posts.Select(p => p.ExternalPostId).ToHashSet();
        var stalePosts = await db.SocialPosts
            .Where(p => p.SocialAccountId == account.Id && !keepIds.Contains(p.ExternalPostId))
            .ToListAsync(ct);
        db.SocialPosts.RemoveRange(stalePosts);

        await db.SaveChangesAsync(ct);

        // Update the in-memory cache
        _cache.Set(account.Id, data);

        _logger.LogInformation("Refreshed social data for account {AccountId} ({Provider}): {PostCount} posts, {AudienceCount} followers",
            account.Id, account.Provider, data.Posts.Count, data.AudienceCount);
    }
}
