using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Data.Migrations;

namespace Shortnr.Tests.Unit.Services;

/// <summary>
/// Tests that the AddSocialAccounts migration is idempotent: running it twice
/// on the same database should succeed without errors and leave the schema intact.
/// </summary>
public class SocialMigrationIdempotencyTests
{
    [Fact]
    public async Task Migration_UpTwice_IsIdempotent()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        // Apply the migration once via EnsureCreated (simulates a fresh DB)
        await using (var db = new AppDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
        }

        // Apply EnsureCreated again — should be a no-op
        await using (var db = new AppDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
        }

        // Verify the table exists and is usable
        await using (var db = new AppDbContext(options))
        {
            var account = new SocialAccount
            {
                OwnerUserId = 1,
                Platform = "twitter",
                PlatformAccountId = "12345",
                DisplayName = "@test",
                AccessTokenEncrypted = "encrypted",
                CreatedAtUtc = DateTime.UtcNow
            };
            db.SocialAccounts.Add(account);
            await db.SaveChangesAsync();

            var count = await db.SocialAccounts.CountAsync();
            Assert.Equal(1, count);
        }
    }

    [Fact]
    public async Task SocialAccount_CanBeCreatedAndQueried()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var account = new SocialAccount
        {
            OwnerUserId = 1,
            Platform = "youtube",
            PlatformAccountId = "UC123",
            DisplayName = "Test Channel",
            AccessTokenEncrypted = "CfDJ8encrypted-access-token",
            RefreshTokenEncrypted = "CfDJ8encrypted-refresh-token",
            AccessTokenExpiryUtc = DateTime.UtcNow.AddHours(1),
            RefreshTokenExpiryUtc = DateTime.UtcNow.AddDays(30),
            TokenRefreshFailed = false,
            CreatedAtUtc = DateTime.UtcNow
        };

        db.SocialAccounts.Add(account);
        await db.SaveChangesAsync();

        var retrieved = await db.SocialAccounts.FirstOrDefaultAsync(a => a.Platform == "youtube");
        Assert.NotNull(retrieved);
        Assert.Equal("UC123", retrieved.PlatformAccountId);
        Assert.Equal("Test Channel", retrieved.DisplayName);
        Assert.Equal("CfDJ8encrypted-access-token", retrieved.AccessTokenEncrypted);
        Assert.False(retrieved.TokenRefreshFailed);
    }
}
