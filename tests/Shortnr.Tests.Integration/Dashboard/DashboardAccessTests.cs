using System.Net;
using Shortnr.Data;
using Shortnr.Tests.Integration.Infrastructure;

namespace Shortnr.Tests.Integration.Dashboard;

/// <summary>
/// Verifies that the dashboard enforces authentication when auth is enabled
/// and is accessible without credentials when auth is disabled.
/// </summary>
public class DashboardAccessTests : IAsyncLifetime
{
    // Each test class owns its factories so DB state is isolated.
    private readonly ShortnrWebAppFactory _authEnabled = new(authEnabled: true);
    private readonly ShortnrWebAppFactory _authDisabled = new(authEnabled: false);

    public async Task InitializeAsync()
    {
        // Ensure the test DB schema is created (migrations run on first client creation).
        using var scope = _authEnabled.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();

        using var scope2 = _authDisabled.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        await db2.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _authEnabled.DisposeAsync();
        await _authDisabled.DisposeAsync();
    }

    // -------------------------------------------------------------------------
    // Auth enabled — unauthenticated
    // -------------------------------------------------------------------------

    [Fact]
    public async Task WhenAuthEnabled_Unauthenticated_FullPageRequest_RedirectsToIndex()
    {
        var client = _authEnabled.CreateClientNoRedirect();

        var response = await client.GetAsync("/dashboard");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task WhenAuthEnabled_Unauthenticated_HtmxRequest_Returns401()
    {
        var client = _authEnabled.CreateClientNoRedirect();
        var request = new HttpRequestMessage(HttpMethod.Get, "/dashboard");
        request.Headers.Add("HX-Request", "true");
        request.Headers.Add("HX-Target", "metrics-summary");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Auth enabled — authenticated
    // -------------------------------------------------------------------------

    [Fact]
    public async Task WhenAuthEnabled_Authenticated_Returns200()
    {
        var authState = _authEnabled.Services.GetRequiredService<TestAuthState>();
        authState.SetAuthenticatedUser("alice", ShortnrWebAppFactory.TestIssuer);

        var client = _authEnabled.CreateClientNoRedirect();
        var response = await client.GetAsync("/dashboard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Auth disabled — no credentials required
    // -------------------------------------------------------------------------

    [Fact]
    public async Task WhenAuthDisabled_Unauthenticated_Returns200()
    {
        var client = _authDisabled.CreateClientNoRedirect();
        var response = await client.GetAsync("/dashboard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
