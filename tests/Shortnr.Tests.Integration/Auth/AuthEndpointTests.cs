using System.Net;
using Shortnr.Tests.Integration.Infrastructure;

namespace Shortnr.Tests.Integration.Auth;

/// <summary>
/// Verifies that <c>/account/login</c> and <c>/account/logout</c> are only
/// registered when <c>Authentication:Enabled</c> is true, and return 404 otherwise.
/// </summary>
public class AuthEndpointTests : IAsyncLifetime
{
    private readonly ShortnrWebAppFactory _authEnabled = new(authEnabled: true);
    private readonly ShortnrWebAppFactory _authDisabled = new(authEnabled: false);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _authEnabled.DisposeAsync();
        await _authDisabled.DisposeAsync();
    }

    // -------------------------------------------------------------------------
    // Auth disabled — endpoints must not be registered
    // -------------------------------------------------------------------------

    [Fact]
    public async Task WhenAuthDisabled_Login_Returns404()
    {
        var client = _authDisabled.CreateClientNoRedirect();
        var response = await client.GetAsync("/account/login");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task WhenAuthDisabled_Logout_Returns404()
    {
        var client = _authDisabled.CreateClientNoRedirect();
        var response = await client.GetAsync("/account/logout");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Auth enabled — endpoints must be registered
    // -------------------------------------------------------------------------

    [Fact]
    public async Task WhenAuthEnabled_Login_IsRegistered()
    {
        var client = _authEnabled.CreateClientNoRedirect();
        var response = await client.GetAsync("/account/login");

        // The OIDC challenge redirects to the (static test) authorization endpoint.
        // We just need to confirm it is NOT 404 — the endpoint exists and challenges.
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task WhenAuthEnabled_Login_WithValidReturnUrl_RedirectsToOidc()
    {
        var client = _authEnabled.CreateClientNoRedirect();
        var response = await client.GetAsync("/account/login?returnUrl=%2Fdashboard");

        // Should be a redirect challenge toward the OIDC authorize endpoint.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(ShortnrWebAppFactory.TestIssuer, response.Headers.Location?.ToString() ?? "");
    }

    [Fact]
    public async Task WhenAuthEnabled_Login_WithAbsoluteReturnUrl_IgnoresReturnUrl()
    {
        // Absolute URLs must be rejected to prevent open redirect attacks.
        var client = _authEnabled.CreateClientNoRedirect();
        var response = await client.GetAsync("/account/login?returnUrl=https%3A%2F%2Fevil.com");

        // Still a challenge, but must not redirect to evil.com.
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("evil.com", response.Headers.Location?.ToString() ?? "");
    }

    [Fact]
    public async Task WhenAuthEnabled_Logout_IsRegistered()
    {
        var client = _authEnabled.CreateClientNoRedirect();
        var response = await client.GetAsync("/account/logout");

        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task WhenAuthEnabled_Logout_RedirectsToRoot()
    {
        // Sign in first so the cookie scheme has something to sign out of.
        var authState = _authEnabled.Services.GetRequiredService<TestAuthState>();
        authState.SetAuthenticatedUser("alice", ShortnrWebAppFactory.TestIssuer);

        var client = _authEnabled.CreateClientNoRedirect();
        var response = await client.GetAsync("/account/logout");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
    }
}
