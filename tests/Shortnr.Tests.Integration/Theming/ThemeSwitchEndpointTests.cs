using System.Net;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Tests.Integration.Infrastructure;
using Shortnr.Web.Features.Theming;

namespace Shortnr.Tests.Integration.Theming;

/// <summary>
/// Verifies the <c>/theme/switch</c> POST endpoint: auth gating and that
/// <c>User.PreferredTheme</c> is persisted (or cleared) correctly. CSRF
/// posture is the same SameSite=Lax session-cookie defense as
/// <c>/workspace/switch</c> — see
/// <c>WorkspaceSwitchEndpointTests.SessionCookie_IsSameSiteLax_BlockingCrossSiteCsrf</c>
/// for that coverage; it isn't duplicated here.
/// </summary>
public class ThemeSwitchEndpointTests : IAsyncLifetime
{
    private readonly ShortnrWebAppFactory _factory = new(authEnabled: true);

    private User _alice = null!;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.Users.RemoveRange(db.Users);
        await db.SaveChangesAsync();

        _alice = new User { Issuer = ShortnrWebAppFactory.TestIssuer, Subject = "alice", Email = "alice@example.com", CreatedAtUtc = DateTime.UtcNow, LastLoginAtUtc = DateTime.UtcNow };
        db.Users.Add(_alice);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private static HttpRequestMessage SwitchPost(string themeId) =>
        new(HttpMethod.Post, "/theme/switch")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["themeId"] = themeId })
        };

    private async Task<string?> ReloadPreferredThemeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users.Where(u => u.Id == _alice.Id).Select(u => u.PreferredTheme).FirstAsync();
    }

    [Fact]
    public async Task PostSwitch_Unauthenticated_Returns401_NoChangePersisted()
    {
        _factory.Services.GetRequiredService<TestAuthState>().ClearUser();
        var client = _factory.CreateClientNoRedirect();

        var response = await client.SendAsync(SwitchPost("midnight"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(await ReloadPreferredThemeAsync());
    }

    [Fact]
    public async Task PostSwitch_ValidPresetTheme_PersistsAndRedirects()
    {
        var authState = _factory.Services.GetRequiredService<TestAuthState>();
        authState.SetAuthenticatedUser("alice", ShortnrWebAppFactory.TestIssuer);
        var client = _factory.CreateClientNoRedirect();

        var response = await client.SendAsync(SwitchPost("midnight"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("midnight", await ReloadPreferredThemeAsync());
    }

    [Fact]
    public async Task PostSwitch_UnknownThemeId_FallsBackToDefault_StoresNull()
    {
        var authState = _factory.Services.GetRequiredService<TestAuthState>();
        authState.SetAuthenticatedUser("alice", ShortnrWebAppFactory.TestIssuer);
        var client = _factory.CreateClientNoRedirect();

        // Set a real preference first, then try to switch to a bogus id.
        await client.SendAsync(SwitchPost("midnight"));
        var response = await client.SendAsync(SwitchPost("not-a-real-theme"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        // ResolveAsync falls back to ThemeCatalog.Default for unknown ids, and
        // the endpoint stores null (not "default") for the default theme.
        Assert.Null(await ReloadPreferredThemeAsync());
    }

    [Fact]
    public async Task PostSwitch_BackToDefault_ClearsStoredPreference()
    {
        var authState = _factory.Services.GetRequiredService<TestAuthState>();
        authState.SetAuthenticatedUser("alice", ShortnrWebAppFactory.TestIssuer);
        var client = _factory.CreateClientNoRedirect();

        await client.SendAsync(SwitchPost("midnight"));
        var response = await client.SendAsync(SwitchPost(ThemeCatalog.DefaultId));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Null(await ReloadPreferredThemeAsync());
    }
}
