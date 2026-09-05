using Shortnr.Data.Entities;

namespace Shortnr.Tests.Unit.Services;

public class SocialAccountMapperTests
{
    private static SocialAccount MakeAccount(
        bool tokenRefreshFailed = false,
        DateTime? accessTokenExpiryUtc = null,
        string platform = "twitter")
    {
        return new SocialAccount
        {
            Id = 1,
            OwnerUserId = 100,
            Platform = platform,
            PlatformAccountId = "12345",
            DisplayName = "@testuser",
            AccessTokenEncrypted = "encrypted",
            RefreshTokenEncrypted = "encrypted-refresh",
            AccessTokenExpiryUtc = accessTokenExpiryUtc,
            TokenRefreshFailed = tokenRefreshFailed,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    [Fact]
    public void ToViewModel_Healthy_WhenNoExpiryAndNotFailed()
    {
        var account = MakeAccount(accessTokenExpiryUtc: null);

        var vm = SocialAccountMapper.ToViewModel(account);

        Assert.Equal(TokenHealthStatus.Healthy, vm.HealthStatus);
        Assert.Equal("Connected", vm.HealthDescription);
    }

    [Fact]
    public void ToViewModel_Healthy_WhenExpiryFarInFuture()
    {
        var account = MakeAccount(accessTokenExpiryUtc: DateTime.UtcNow.AddDays(7));

        var vm = SocialAccountMapper.ToViewModel(account);

        Assert.Equal(TokenHealthStatus.Healthy, vm.HealthStatus);
        Assert.Contains("days", vm.HealthDescription);
    }

    [Fact]
    public void ToViewModel_ExpiringSoon_WhenExpiryWithinWindow()
    {
        var account = MakeAccount(accessTokenExpiryUtc: DateTime.UtcNow.AddHours(12));

        var vm = SocialAccountMapper.ToViewModel(account, refreshWindowHours: 24);

        Assert.Equal(TokenHealthStatus.ExpiringSoon, vm.HealthStatus);
        Assert.Contains("expiring soon", vm.HealthDescription);
    }

    [Fact]
    public void ToViewModel_ExpiringSoon_WhenTokenAlreadyExpired()
    {
        var account = MakeAccount(accessTokenExpiryUtc: DateTime.UtcNow.AddHours(-1));

        var vm = SocialAccountMapper.ToViewModel(account);

        Assert.Equal(TokenHealthStatus.ExpiringSoon, vm.HealthStatus);
        Assert.Contains("expired", vm.HealthDescription);
    }

    [Fact]
    public void ToViewModel_RefreshFailed_WhenFlagSet()
    {
        var account = MakeAccount(tokenRefreshFailed: true);

        var vm = SocialAccountMapper.ToViewModel(account);

        Assert.Equal(TokenHealthStatus.RefreshFailed, vm.HealthStatus);
        Assert.Contains("re-link required", vm.HealthDescription);
    }

    [Fact]
    public void ToViewModel_RefreshFailed_TakesPriorityOverExpiring()
    {
        var account = MakeAccount(
            tokenRefreshFailed: true,
            accessTokenExpiryUtc: DateTime.UtcNow.AddHours(1));

        var vm = SocialAccountMapper.ToViewModel(account);

        Assert.Equal(TokenHealthStatus.RefreshFailed, vm.HealthStatus);
    }

    [Fact]
    public void ToViewModel_CopiesIdPlatformAndDisplayName()
    {
        var account = MakeAccount(platform: "youtube");
        account.DisplayName = "Test Channel";
        account.Id = 42;

        var vm = SocialAccountMapper.ToViewModel(account);

        Assert.Equal(42, vm.Id);
        Assert.Equal("youtube", vm.Platform);
        Assert.Equal("Test Channel", vm.DisplayName);
    }

    [Fact]
    public void ToViewModel_ExpiryTimePreserved()
    {
        var expiry = new DateTime(2026, 12, 25, 10, 0, 0, DateTimeKind.Utc);
        var account = MakeAccount(accessTokenExpiryUtc: expiry);

        var vm = SocialAccountMapper.ToViewModel(account);

        Assert.Equal(expiry, vm.AccessTokenExpiryUtc);
    }
}
