using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Web.Features.Social;

namespace Shortnr.Tests.Unit.Services;

public class SocialAccountServiceTests
{
    private static (SocialAccountService Service, AppDbContext Db, string DatabaseName) CreateService()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        var db = new AppDbContext(options);

        var dpProvider = DataProtectionProvider.Create("test-app");
        var encryption = new SocialTokenEncryptionService(dpProvider);
        var service = new SocialAccountService(db, encryption);

        return (service, db, dbName);
    }

    private static async Task<User> SeedUserAsync(AppDbContext db, long id = 100)
    {
        var user = new User { Id = id, Issuer = "test", Subject = "test-subject", Email = "test@test.com" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task UpsertAsync_EncryptsTokensBeforePersisting()
    {
        var (service, db, dbName) = CreateService();
        await SeedUserAsync(db);

        var result = await service.UpsertAsync(
            ownerUserId: 100,
            platform: "twitter",
            platformAccountId: "12345",
            displayName: "@testuser",
            accessToken: "plain-access-token",
            refreshToken: "plain-refresh-token",
            accessTokenExpiryUtc: DateTime.UtcNow.AddHours(1),
            refreshTokenExpiryUtc: DateTime.UtcNow.AddDays(30));

        // The returned entity has decrypted tokens (service decrypts on read)
        Assert.Equal("plain-access-token", result.AccessTokenEncrypted);
        Assert.Equal("plain-refresh-token", result.RefreshTokenEncrypted);

        // Open a fresh DbContext to read the raw DB state
        var freshOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        using var freshDb = new AppDbContext(freshOptions);
        var raw = await freshDb.SocialAccounts.FindAsync(result.Id);

        Assert.NotNull(raw);
        Assert.NotEqual("plain-access-token", raw.AccessTokenEncrypted);
        Assert.StartsWith("CfDJ", raw.AccessTokenEncrypted);
        Assert.StartsWith("CfDJ", raw.RefreshTokenEncrypted!);
    }

    [Fact]
    public async Task GetByOwnerAsync_DecryptsTokensOnRead()
    {
        var (service, db, _) = CreateService();
        await SeedUserAsync(db);

        await service.UpsertAsync(100, "twitter", "12345", "@testuser",
            "my-token", "my-refresh", DateTime.UtcNow.AddHours(1), DateTime.UtcNow.AddDays(30));

        var accounts = await service.GetByOwnerAsync(100);

        Assert.Single(accounts);
        Assert.Equal("my-token", accounts[0].AccessTokenEncrypted);
        Assert.Equal("my-refresh", accounts[0].RefreshTokenEncrypted);
    }

    [Fact]
    public async Task UpsertAsync_UpdatesExistingAccount()
    {
        var (service, db, _) = CreateService();
        await SeedUserAsync(db);

        await service.UpsertAsync(100, "twitter", "12345", "@oldname",
            "old-token", null, null, null);

        var updated = await service.UpsertAsync(100, "twitter", "12345", "@newname",
            "new-token", "new-refresh", DateTime.UtcNow.AddHours(2), DateTime.UtcNow.AddDays(60));

        Assert.Equal("@newname", updated.DisplayName);
        Assert.Equal("new-token", updated.AccessTokenEncrypted);

        var accounts = await service.GetByOwnerAsync(100);
        Assert.Single(accounts);
    }

    [Fact]
    public async Task DeleteAsync_RemovesAccount()
    {
        var (service, db, _) = CreateService();
        await SeedUserAsync(db);

        var created = await service.UpsertAsync(100, "twitter", "12345", "@testuser",
            "token", null, null, null);

        var deleted = await service.DeleteAsync(created.Id, 100);

        Assert.True(deleted);
        Assert.Empty(await db.SocialAccounts.ToListAsync());
    }

    [Fact]
    public async Task DeleteAsync_ReturnsFalseForNonexistentAccount()
    {
        var (service, db, _) = CreateService();
        await SeedUserAsync(db);

        var deleted = await service.DeleteAsync(999, 100);

        Assert.False(deleted);
    }

    [Fact]
    public async Task GetExpiringAsync_ReturnsAccountsWithExpiringTokens()
    {
        var (service, db, _) = CreateService();
        await SeedUserAsync(db);

        await service.UpsertAsync(100, "twitter", "tw1", "@user1",
            "token1", "refresh1", DateTime.UtcNow.AddHours(12), null);

        await service.UpsertAsync(100, "youtube", "yt1", "@user2",
            "token2", "refresh2", DateTime.UtcNow.AddHours(48), null);

        await service.UpsertAsync(100, "instagram", "ig1", "@user3",
            "token3", null, null, null);

        var expiring = await service.GetExpiringAsync(refreshWindowHours: 24);

        Assert.Single(expiring);
        Assert.Equal("twitter", expiring[0].Platform);
    }

    [Fact]
    public async Task GetExpiringAsync_ExcludesFailedAccounts()
    {
        var (service, db, _) = CreateService();
        await SeedUserAsync(db);

        var account = await service.UpsertAsync(100, "twitter", "tw1", "@user1",
            "token1", "refresh1", DateTime.UtcNow.AddHours(1), null);

        await service.MarkRefreshFailedAsync(account.Id);

        var expiring = await service.GetExpiringAsync(refreshWindowHours: 24);

        Assert.Empty(expiring);
    }

    [Fact]
    public async Task GetExpiringAsync_ExcludesAccountsWithoutRefreshToken()
    {
        var (service, db, _) = CreateService();
        await SeedUserAsync(db);

        await service.UpsertAsync(100, "instagram", "ig1", "@user1",
            "token1", null, DateTime.UtcNow.AddHours(1), null);

        var expiring = await service.GetExpiringAsync(refreshWindowHours: 24);

        Assert.Empty(expiring);
    }

    [Fact]
    public async Task UpdateTokensAfterRefreshAsync_UpdatesTokensAndClearsFailedFlag()
    {
        var (service, db, _) = CreateService();
        await SeedUserAsync(db);

        var account = await service.UpsertAsync(100, "twitter", "tw1", "@user1",
            "old-token", "old-refresh", DateTime.UtcNow.AddHours(1), null);

        await service.MarkRefreshFailedAsync(account.Id);

        await service.UpdateTokensAfterRefreshAsync(
            account.Id,
            "new-token",
            "new-refresh",
            DateTime.UtcNow.AddHours(2),
            DateTime.UtcNow.AddDays(30));

        var updated = await service.GetByIdAsync(account.Id, 100);
        Assert.NotNull(updated);
        Assert.Equal("new-token", updated.AccessTokenEncrypted);
        Assert.Equal("new-refresh", updated.RefreshTokenEncrypted);
        Assert.False(updated.TokenRefreshFailed);
        Assert.NotNull(updated.LastRefreshedAtUtc);
    }

    [Fact]
    public async Task MarkRefreshFailedAsync_SetsFlag()
    {
        var (service, db, _) = CreateService();
        await SeedUserAsync(db);

        var account = await service.UpsertAsync(100, "twitter", "tw1", "@user1",
            "token", "refresh", null, null);

        await service.MarkRefreshFailedAsync(account.Id);

        var raw = await db.SocialAccounts.FindAsync(account.Id);
        Assert.NotNull(raw);
        Assert.True(raw.TokenRefreshFailed);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNullForNonexistentAccount()
    {
        var (service, db, _) = CreateService();
        await SeedUserAsync(db);

        var result = await service.GetByIdAsync(999, 100);

        Assert.Null(result);
    }
}
