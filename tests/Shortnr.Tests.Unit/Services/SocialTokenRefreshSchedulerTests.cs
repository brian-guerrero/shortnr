using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Web.Features.Social;

namespace Shortnr.Tests.Unit.Services;

/// <summary>
/// Tests the social token refresh background scheduler behavior.
/// Verifies that it refreshes expiring tokens and handles failures gracefully.
/// </summary>
public class SocialTokenRefreshSchedulerTests
{
    private static (SocialAccountService Service, AppDbContext Db) CreateService()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new AppDbContext(options);

        var dpProvider = DataProtectionProvider.Create("test-app");
        var encryption = new SocialTokenEncryptionService(dpProvider);
        var service = new SocialAccountService(db, encryption);

        return (service, db);
    }

    private static async Task<User> SeedUserAsync(AppDbContext db, long id = 100)
    {
        var user = new User { Id = id, Issuer = "test", Subject = "test-subject", Email = "test@test.com" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task GetExpiringAsync_FindsTokensNeedingRefresh()
    {
        var (service, db) = CreateService();
        await SeedUserAsync(db);

        // Token expiring in 6 hours (within 24h window)
        await service.UpsertAsync(100, "twitter", "tw1", "@user1",
            "token1", "refresh1", DateTime.UtcNow.AddHours(6), null);

        // Token expiring in 2 hours (within 24h window)
        await service.UpsertAsync(100, "youtube", "yt1", "@user2",
            "token2", "refresh2", DateTime.UtcNow.AddHours(2), null);

        // Token not expiring for 30 days (outside window)
        await service.UpsertAsync(100, "instagram", "ig1", "@user3",
            "token3", "refresh3", DateTime.UtcNow.AddDays(30), null);

        var expiring = await service.GetExpiringAsync(refreshWindowHours: 24);

        Assert.Equal(2, expiring.Count);
        Assert.Contains(expiring, a => a.Platform == "twitter");
        Assert.Contains(expiring, a => a.Platform == "youtube");
    }

    [Fact]
    public async Task GetExpiringAsync_WithZeroWindow_CatchesExpiredTokens()
    {
        var (service, db) = CreateService();
        await SeedUserAsync(db);

        // Already expired token
        await service.UpsertAsync(100, "twitter", "tw1", "@user1",
            "token1", "refresh1", DateTime.UtcNow.AddHours(-1), null);

        var expiring = await service.GetExpiringAsync(refreshWindowHours: 0);

        Assert.Single(expiring);
    }

    [Fact]
    public async Task GetExpiringAsync_SkipsAccountsWithoutRefreshToken()
    {
        var (service, db) = CreateService();
        await SeedUserAsync(db);

        // Expiring but no refresh token
        await service.UpsertAsync(100, "twitter", "tw1", "@user1",
            "token1", null, DateTime.UtcNow.AddHours(1), null);

        var expiring = await service.GetExpiringAsync(refreshWindowHours: 24);

        Assert.Empty(expiring);
    }

    [Fact]
    public async Task GetExpiringAsync_SkipsFailedAccounts()
    {
        var (service, db) = CreateService();
        await SeedUserAsync(db);

        var account = await service.UpsertAsync(100, "twitter", "tw1", "@user1",
            "token1", "refresh1", DateTime.UtcNow.AddHours(1), null);

        await service.MarkRefreshFailedAsync(account.Id);

        var expiring = await service.GetExpiringAsync(refreshWindowHours: 24);

        Assert.Empty(expiring);
    }

    [Fact]
    public async Task RefreshFailure_SetsFailedFlag_AndStopsFurtherRefreshAttempts()
    {
        var (service, db) = CreateService();
        await SeedUserAsync(db);

        var account = await service.UpsertAsync(100, "twitter", "tw1", "@user1",
            "token1", "refresh1", DateTime.UtcNow.AddHours(1), null);

        // Simulate failure
        await service.MarkRefreshFailedAsync(account.Id);

        // Account should no longer appear in expiring list
        var expiring = await service.GetExpiringAsync(refreshWindowHours: 24);
        Assert.Empty(expiring);

        // Verify the failed flag is set
        var raw = await db.SocialAccounts.FindAsync(account.Id);
        Assert.True(raw!.TokenRefreshFailed);
    }

    [Fact]
    public async Task SuccessfulRefresh_ClearsFailedFlagAndUpdatesTokens()
    {
        var (service, db) = CreateService();
        await SeedUserAsync(db);

        var account = await service.UpsertAsync(100, "twitter", "tw1", "@user1",
            "old-token", "old-refresh", DateTime.UtcNow.AddHours(1), null);

        await service.MarkRefreshFailedAsync(account.Id);

        // Simulate successful refresh
        await service.UpdateTokensAfterRefreshAsync(
            account.Id,
            "new-token",
            "new-refresh",
            DateTime.UtcNow.AddHours(2),
            DateTime.UtcNow.AddDays(30));

        var refreshed = await service.GetByIdAsync(account.Id, 100);
        Assert.NotNull(refreshed);
        Assert.False(refreshed.TokenRefreshFailed);
        Assert.Equal("new-token", refreshed.AccessTokenEncrypted);
        Assert.Equal("new-refresh", refreshed.RefreshTokenEncrypted);
    }
}
