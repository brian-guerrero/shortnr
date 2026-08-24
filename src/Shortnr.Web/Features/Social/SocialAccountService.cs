using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Web.Features.Authentication;

namespace Shortnr.Web.Features.Social;

/// <summary>
/// Service for managing linked social accounts (PRD-021).
/// Handles linking/unlinking accounts, OAuth flows, and triggering background refresh.
/// </summary>
public class SocialAccountService
{
    private readonly AppDbContext _db;
    private readonly IUserIdentityService _identity;
    private readonly IEnumerable<ISocialPlatformProvider> _providers;
    private readonly Channel<SocialFetchRequest> _fetchChannel;
    private readonly ILogger<SocialAccountService> _logger;

    public SocialAccountService(
        AppDbContext db,
        IUserIdentityService identity,
        IEnumerable<ISocialPlatformProvider> providers,
        Channel<SocialFetchRequest> fetchChannel,
        ILogger<SocialAccountService> logger)
    {
        _db = db;
        _identity = identity;
        _providers = providers;
        _fetchChannel = fetchChannel;
        _logger = logger;
    }

    /// <summary>
    /// Returns the linked social accounts for the current user (or workspace).
    /// </summary>
    public async Task<IReadOnlyList<SocialAccount>> GetLinkedAccountsAsync(ClaimsPrincipal principal, long? workspaceId = null)
    {
        var userId = await _identity.ResolveOwnerUserIdAsync(principal);
        if (userId is null) return [];

        return await _db.SocialAccounts
            .AsNoTracking()
            .Where(a => a.IsLinked &&
                        (workspaceId.HasValue ? a.WorkspaceId == workspaceId.Value : a.OwnerUserId == userId.Value))
            .OrderBy(a => a.Provider)
            .ToListAsync();
    }

    /// <summary>
    /// Returns the social account for a specific provider and scope.
    /// </summary>
    public async Task<SocialAccount?> GetAccountAsync(SocialProvider provider, long userId, long? workspaceId = null)
    {
        return await _db.SocialAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Provider == provider &&
                                      a.IsLinked &&
                                      (workspaceId.HasValue ? a.WorkspaceId == workspaceId.Value : a.OwnerUserId == userId));
    }

    /// <summary>
    /// Links a social account after successful OAuth. Creates or updates the
    /// SocialAccount row and triggers an initial data fetch.
    /// </summary>
    public async Task<bool> LinkAccountAsync(ClaimsPrincipal principal, SocialProvider provider, OAuthTokens tokens, long? workspaceId = null)
    {
        var userId = await _identity.ResolveOwnerUserIdAsync(principal);
        if (userId is null) return false;

        var existing = await _db.SocialAccounts
            .FirstOrDefaultAsync(a => a.Provider == provider &&
                                      (workspaceId.HasValue ? a.WorkspaceId == workspaceId.Value : a.OwnerUserId == userId.Value));

        if (existing is not null)
        {
            existing.ExternalId = tokens.ExternalId ?? existing.ExternalId;
            existing.Username = tokens.Username ?? existing.Username;
            existing.DisplayName = tokens.DisplayName ?? existing.DisplayName;
            existing.AvatarUrl = tokens.AvatarUrl ?? existing.AvatarUrl;
            existing.AccessTokenEncrypted = tokens.AccessToken;
            existing.RefreshTokenEncrypted = tokens.RefreshToken;
            existing.TokenExpiresUtc = tokens.ExpiresAtUtc;
            existing.IsLinked = true;
            existing.LastError = null;
            existing.UpdatedAtUtc = DateTime.UtcNow;
        }
        else
        {
            existing = new SocialAccount
            {
                Provider = provider,
                OwnerUserId = workspaceId.HasValue ? null : userId.Value,
                WorkspaceId = workspaceId,
                ExternalId = tokens.ExternalId ?? string.Empty,
                Username = tokens.Username ?? string.Empty,
                DisplayName = tokens.DisplayName,
                AvatarUrl = tokens.AvatarUrl,
                AccessTokenEncrypted = tokens.AccessToken,
                RefreshTokenEncrypted = tokens.RefreshToken,
                TokenExpiresUtc = tokens.ExpiresAtUtc,
                IsLinked = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            _db.SocialAccounts.Add(existing);
        }

        await _db.SaveChangesAsync();

        // Trigger initial data fetch
        _fetchChannel.Writer.TryWrite(new SocialFetchRequest { SocialAccountId = existing.Id });

        _logger.LogInformation("Linked {Provider} account for user {UserId}: {Username}",
            provider, userId.Value, existing.Username);
        return true;
    }

    /// <summary>
    /// Unlinks a social account. Removes the account and its cached posts.
    /// </summary>
    public async Task<bool> UnlinkAccountAsync(ClaimsPrincipal principal, SocialProvider provider, long? workspaceId = null)
    {
        var userId = await _identity.ResolveOwnerUserIdAsync(principal);
        if (userId is null) return false;

        var account = await _db.SocialAccounts
            .FirstOrDefaultAsync(a => a.Provider == provider &&
                                      (workspaceId.HasValue ? a.WorkspaceId == workspaceId.Value : a.OwnerUserId == userId.Value));

        if (account is null) return false;

        _db.SocialAccounts.Remove(account);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Unlinked {Provider} account for user {UserId}", provider, userId.Value);
        return true;
    }

    /// <summary>
    /// Triggers a background refresh for a specific social account.
    /// </summary>
    public void TriggerRefresh(long socialAccountId)
    {
        _fetchChannel.Writer.TryWrite(new SocialFetchRequest { SocialAccountId = socialAccountId });
    }

    /// <summary>
    /// Gets the provider for a given SocialProvider enum value.
    /// </summary>
    public ISocialPlatformProvider? GetProvider(SocialProvider provider) =>
        _providers.FirstOrDefault(p => p.Provider == provider);
}
