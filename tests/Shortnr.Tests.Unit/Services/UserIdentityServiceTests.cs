using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Web.Services;

namespace Shortnr.Tests.Unit.Services;

public class UserIdentityServiceTests : IDisposable
{
    private const string TestIssuer = "http://test.issuer";

    private readonly AppDbContext _db;

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
        var sut = new UserIdentityService(_db, new ConfigurationBuilder().Build());
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
    // Helpers
    // -------------------------------------------------------------------------

    private UserIdentityService BuildService(bool authEnabled) =>
        new(_db, new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Enabled"] = authEnabled.ToString().ToLower(),
                ["Authentication:Oidc:Authority"] = TestIssuer
            })
            .Build());

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
