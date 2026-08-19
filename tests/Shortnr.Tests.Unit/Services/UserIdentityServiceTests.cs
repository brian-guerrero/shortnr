using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Web.Features.Theming;

namespace Shortnr.Tests.Unit.Services;

public class UserIdentityServiceTests : IDisposable
{
    private const string TestIssuer = "http://test.issuer";

    private readonly AppDbContext _db;
    private readonly IThemeResolver _themeResolver = new ThemeResolver([ThemeCatalog.Instance]);

    public UserIdentityServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    // -------------------------------------------------------------------------
    // IsAuthEnabled
    // -------------------------------------------------------------------------

    [Fact]
    public void IsAuthEnabled_WhenConfigTrue_ReturnsTrue()
    {
        var sut = BuildService(authEnabled: true);
        Assert.True(sut.IsAuthEnabled);
    }

    [Fact]
    public void IsAuthEnabled_WhenConfigFalse_ReturnsFalse()
    {
        var sut = BuildService(authEnabled: false);
        Assert.False(sut.IsAuthEnabled);
    }

    [Fact]
    public void IsAuthEnabled_WhenKeyAbsent_DefaultsToTrue()
    {
        var sut = new UserIdentityService(_db, new ConfigurationBuilder().Build(), new HttpContextAccessor(), _themeResolver);
        Assert.True(sut.IsAuthEnabled);
    }

    // -------------------------------------------------------------------------
    // ResolveOwnerUserIdAsync — short-circuit cases
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ResolveOwnerUserIdAsync_WhenAuthDisabled_ReturnsNull()
    {
        var sut = BuildService(authEnabled: false);

        var result = await sut.ResolveOwnerUserIdAsync(AuthenticatedPrincipal("sub-1"));

        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveOwnerUserIdAsync_WhenUserNotAuthenticated_ReturnsNull()
    {
        var sut = BuildService(authEnabled: true);
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity()); // no authentication type

        var result = await sut.ResolveOwnerUserIdAsync(anonymous);

        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveOwnerUserIdAsync_WhenSubjectClaimMissing_ReturnsNull()
    {
        var sut = BuildService(authEnabled: true);
        // authenticated but carries no NameIdentifier or "sub" claim
        var noSubject = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Email, "test@example.com")],
            authenticationType: "test"));

        var result = await sut.ResolveOwnerUserIdAsync(noSubject);

        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveOwnerUserIdAsync_WhenUserNotInDatabase_ReturnsNull()
    {
        var sut = BuildService(authEnabled: true);

        var result = await sut.ResolveOwnerUserIdAsync(AuthenticatedPrincipal("nobody"));

        Assert.Null(result);
    }

    // -------------------------------------------------------------------------
    // ResolveOwnerUserIdAsync — happy paths
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ResolveOwnerUserIdAsync_WhenUserFoundByNameIdentifier_ReturnsId()
    {
        var user = await SeedUser("sub-ni", TestIssuer);
        var sut = BuildService(authEnabled: true);
        var principal = AuthenticatedPrincipal("sub-ni", useNameIdentifier: true);

        var result = await sut.ResolveOwnerUserIdAsync(principal);

        Assert.Equal(user.Id, result);
    }

    [Fact]
    public async Task ResolveOwnerUserIdAsync_WhenUserFoundBySubClaim_ReturnsId()
    {
        var user = await SeedUser("sub-plain", TestIssuer);
        var sut = BuildService(authEnabled: true);
        var principal = AuthenticatedPrincipal("sub-plain", useNameIdentifier: false);

        var result = await sut.ResolveOwnerUserIdAsync(principal);

        Assert.Equal(user.Id, result);
    }

    [Fact]
    public async Task ResolveOwnerUserIdAsync_MatchesOnBothIssuerAndSubject()
    {
        // Two users with the same subject value but different issuers.
        var userA = await SeedUser("shared-sub", TestIssuer);
        await SeedUser("shared-sub", "http://other.issuer");

        var sut = BuildService(authEnabled: true);

        var result = await sut.ResolveOwnerUserIdAsync(AuthenticatedPrincipal("shared-sub"));

        // Must return the row that matches the configured TestIssuer, not the other one.
        Assert.Equal(userA.Id, result);
    }

    [Fact]
    public async Task ResolveOwnerUserIdAsync_DifferentSubject_SameIssuer_ReturnsCorrectId()
    {
        var userA = await SeedUser("alice", TestIssuer);
        var userB = await SeedUser("bob", TestIssuer);
        var sut = BuildService(authEnabled: true);

        var resultA = await sut.ResolveOwnerUserIdAsync(AuthenticatedPrincipal("alice"));
        var resultB = await sut.ResolveOwnerUserIdAsync(AuthenticatedPrincipal("bob"));

        Assert.Equal(userA.Id, resultA);
        Assert.Equal(userB.Id, resultB);
        Assert.NotEqual(resultA, resultB);
    }

    // -------------------------------------------------------------------------
    // ResolveThemePreferenceAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ResolveThemePreferenceAsync_WhenUnauthenticated_ReturnsDefault()
    {
        var sut = BuildService(authEnabled: true);
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        var theme = await sut.ResolveThemePreferenceAsync(anonymous);

        Assert.Equal(ThemeCatalog.Default, theme);
    }

    [Fact]
    public async Task ResolveThemePreferenceAsync_WhenUserHasNoPreference_ReturnsDefault()
    {
        var user = await SeedUser("no-pref", TestIssuer);
        var sut = BuildService(authEnabled: true);

        var theme = await sut.ResolveThemePreferenceAsync(AuthenticatedPrincipal("no-pref"));

        Assert.Equal(ThemeCatalog.Default, theme);
    }

    [Fact]
    public async Task ResolveThemePreferenceAsync_WhenUserHasValidPreference_ReturnsResolvedTheme()
    {
        var user = await SeedUser("has-pref", TestIssuer);
        user.PreferredTheme = "midnight";
        await _db.SaveChangesAsync();
        var sut = BuildService(authEnabled: true);

        var theme = await sut.ResolveThemePreferenceAsync(AuthenticatedPrincipal("has-pref"));

        Assert.Equal("midnight", theme.Id);
    }

    [Fact]
    public async Task ResolveThemePreferenceAsync_WhenStoredPreferenceIsInvalid_FallsBackToDefault()
    {
        var user = await SeedUser("bad-pref", TestIssuer);
        user.PreferredTheme = "not-a-real-theme";
        await _db.SaveChangesAsync();
        var sut = BuildService(authEnabled: true);

        var theme = await sut.ResolveThemePreferenceAsync(AuthenticatedPrincipal("bad-pref"));

        Assert.Equal(ThemeCatalog.Default, theme);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private UserIdentityService BuildService(bool authEnabled) =>
        new(_db, new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Enabled"] = authEnabled.ToString().ToLower(),
                ["Authentication:Oidc:Authority"] = TestIssuer
            })
            .Build(), new HttpContextAccessor(), _themeResolver);

    private static ClaimsPrincipal AuthenticatedPrincipal(string subject, bool useNameIdentifier = true) =>
        new(new ClaimsIdentity(
            [new Claim(useNameIdentifier ? ClaimTypes.NameIdentifier : "sub", subject)],
            authenticationType: "test"));

    private async Task<User> SeedUser(string subject, string issuer)
    {
        var user = new User
        {
            Subject = subject,
            Issuer = issuer,
            CreatedAtUtc = DateTime.UtcNow,
            LastLoginAtUtc = DateTime.UtcNow
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }
}
