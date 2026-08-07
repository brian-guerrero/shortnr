using System.Net;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Tests.Integration.Infrastructure;

namespace Shortnr.Tests.Integration.Workspaces;

/// <summary>
/// Verifies the <c>/workspace/switch</c> POST endpoint: auth + membership gating,
/// cookie attributes, and the CSRF posture. The endpoint deliberately carries no
/// antiforgery token; cross-origin CSRF is blocked by the SameSite=Lax session
/// cookie (see <see cref="SessionCookie_IsSameSiteLax"/>) plus membership
/// validation before any cookie write.
/// </summary>
public class WorkspaceSwitchEndpointTests : IAsyncLifetime
{
    private readonly ShortnrWebAppFactory _factory = new(authEnabled: true);

    private User _alice = null!;
    private User _bob = null!;
    private Workspace _workspace = null!;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.WorkspaceMembers.RemoveRange(db.WorkspaceMembers);
        db.Workspaces.RemoveRange(db.Workspaces);
        db.Users.RemoveRange(db.Users);
        await db.SaveChangesAsync();

        _alice = new User { Issuer = ShortnrWebAppFactory.TestIssuer, Subject = "alice", Email = "alice@example.com", CreatedAtUtc = DateTime.UtcNow, LastLoginAtUtc = DateTime.UtcNow };
        _bob = new User { Issuer = ShortnrWebAppFactory.TestIssuer, Subject = "bob", Email = "bob@example.com", CreatedAtUtc = DateTime.UtcNow, LastLoginAtUtc = DateTime.UtcNow };
        db.Users.AddRange(_alice, _bob);
        await db.SaveChangesAsync();

        _workspace = new Workspace { Name = "Acme", Slug = "acme", OwnerUserId = _alice.Id, CreatedAtUtc = DateTime.UtcNow };
        db.Workspaces.Add(_workspace);
        await db.SaveChangesAsync();

        db.WorkspaceMembers.Add(new WorkspaceMember
        {
            WorkspaceId = _workspace.Id,
            UserId = _alice.Id,
            Role = WorkspaceRole.Owner,
            InvitedAtUtc = DateTime.UtcNow,
            JoinedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private static HttpRequestMessage SwitchPost(string slug) =>
        new(HttpMethod.Post, "/workspace/switch")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["slug"] = slug })
        };

    private string? GetSwitchCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(v => v.StartsWith("snr_workspace=", StringComparison.OrdinalIgnoreCase))
            : null;

    // -------------------------------------------------------------------------
    // Auth + membership gating
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PostSwitch_Unauthenticated_Returns401_NoCookieSet()
    {
        _factory.Services.GetRequiredService<TestAuthState>().ClearUser();
        var client = _factory.CreateClientNoRedirect();

        var response = await client.SendAsync(SwitchPost("acme"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(GetSwitchCookie(response));
    }

    [Fact]
    public async Task PostSwitch_NonMember_RedirectsWithoutSettingCookie()
    {
        var authState = _factory.Services.GetRequiredService<TestAuthState>();
        authState.SetAuthenticatedUser("bob", ShortnrWebAppFactory.TestIssuer);
        var client = _factory.CreateClientNoRedirect();

        var response = await client.SendAsync(SwitchPost("acme"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        // Bob knows the slug but is not a member — no cookie is written.
        Assert.Null(GetSwitchCookie(response));
    }

    [Fact]
    public async Task PostSwitch_UnknownSlug_RedirectsWithoutSettingCookie()
    {
        var authState = _factory.Services.GetRequiredService<TestAuthState>();
        authState.SetAuthenticatedUser("alice", ShortnrWebAppFactory.TestIssuer);
        var client = _factory.CreateClientNoRedirect();

        var response = await client.SendAsync(SwitchPost("no-such-workspace"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Null(GetSwitchCookie(response));
    }

    // -------------------------------------------------------------------------
    // Happy path + cookie attributes
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PostSwitch_AsMember_SetsHttpOnlySameSiteLaxCookie()
    {
        var authState = _factory.Services.GetRequiredService<TestAuthState>();
        authState.SetAuthenticatedUser("alice", ShortnrWebAppFactory.TestIssuer);
        var client = _factory.CreateClientNoRedirect();

        var response = await client.SendAsync(SwitchPost("acme"));
        var setCookie = GetSwitchCookie(response);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(setCookie);
        // The switch cookie itself is HttpOnly + SameSite=Lax, so it is never
        // readable by script and never attached to cross-site requests.
        Assert.StartsWith("snr_workspace=acme;", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PostSwitch_Personal_DeletesWorkspaceCookie()
    {
        var authState = _factory.Services.GetRequiredService<TestAuthState>();
        authState.SetAuthenticatedUser("alice", ShortnrWebAppFactory.TestIssuer);
        var client = _factory.CreateClientNoRedirect();

        var response = await client.SendAsync(SwitchPost("personal"));
        var setCookie = GetSwitchCookie(response);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(setCookie);
        // Deleting a cookie emits an expired empty value.
        Assert.Contains("snr_workspace=", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expires=", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------------
    // CSRF posture
    // -------------------------------------------------------------------------

    [Fact]
    public void SessionCookie_IsSameSiteLax_BlockingCrossSiteCsrf()
    {
        // The /workspace/switch endpoint writes no antiforgery token; its CSRF
        // defense relies on the session cookie being SameSite=Lax. Modern browsers
        // refuse to attach a Lax cookie to a cross-site POST, so a cross-origin
        // forge request arrives unauthenticated and is rejected with 401 before
        // any cookie write. Pin that default here so a future config change that
        // weakens SameSite is caught.
        var cookieOptions = _factory.Services.GetRequiredService<IOptions<CookieAuthenticationOptions>>();

        Assert.Equal(SameSiteMode.Lax, cookieOptions.Value.Cookie.SameSite);
    }
}
