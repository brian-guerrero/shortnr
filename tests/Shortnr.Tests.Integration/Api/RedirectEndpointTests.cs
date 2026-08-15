using System.Net;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Tests.Integration.Infrastructure;

namespace Shortnr.Tests.Integration.Api;

/// <summary>
/// Verifies that the redirect endpoint resolves short codes per the incoming
/// Host header: a verified custom domain scopes lookups to that domain, while
/// any other host resolves codes created under the instance default domain.
/// Also covers the /.well-known/shortnr-verify.txt domain-verification endpoint.
/// </summary>
public class RedirectEndpointTests : IAsyncLifetime
{
    private readonly ShortnrWebAppFactory _factory = new(authEnabled: false);
    private Domain _domain = null!;

    public Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.ClickEvents.RemoveRange(db.ClickEvents);
        db.ShortenedUrls.RemoveRange(db.ShortenedUrls);
        db.Domains.RemoveRange(db.Domains);
        db.Users.RemoveRange(db.Users);

        _domain = new Domain
        {
            Hostname = "go.example.com",
            IsVerified = true,
            IsDefault = true,
            VerificationToken = "tok-abc",
            CreatedAtUtc = DateTime.UtcNow
        };
        db.Domains.Add(_domain);
        db.SaveChanges();

        db.ShortenedUrls.AddRange(
            new ShortenedUrl { LongUrl = "https://example.com/default", ShortCode = "default", CreatedAtUtc = DateTime.UtcNow },
            new ShortenedUrl { LongUrl = "https://example.com/custom", ShortCode = "custom", DomainId = _domain.Id, CreatedAtUtc = DateTime.UtcNow });
        db.SaveChanges();

        return Task.CompletedTask;
    }

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    [Fact]
    public async Task Redirect_DefaultHostUnknownCode_ReturnsNotFound()
    {
        var response = await _factory.CreateClient().GetAsync("/nope");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Redirect_DefaultHost_ResolvesNullDomainCode()
    {
        var client = _factory.CreateClientNoRedirect();
        var response = await client.GetAsync("/default");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("https://example.com/default", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Redirect_CustomDomainHost_ResolvesDomainScopedCode()
    {
        var client = _factory.CreateClientNoRedirect();
        var request = new HttpRequestMessage(HttpMethod.Get, "/custom");
        request.Headers.Host = "go.example.com";

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("https://example.com/custom", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Redirect_ArchivedLink_ReturnsGone()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ShortenedUrls.Add(new ShortenedUrl
            {
                LongUrl = "https://example.com/archived",
                ShortCode = "archived",
                ArchivedAtUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClientNoRedirect();
        var response = await client.GetAsync("/archived");

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
    }

    [Fact]
    public async Task Redirect_DefaultHost_DoesNotResolveDomainScopedCode()
    {
        var client = _factory.CreateClientNoRedirect();
        var response = await client.GetAsync("/custom");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Redirect_CustomDomainHost_DoesNotResolveDefaultDomainCode()
    {
        var client = _factory.CreateClientNoRedirect();
        var request = new HttpRequestMessage(HttpMethod.Get, "/default");
        request.Headers.Host = "go.example.com";

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Redirect_UnverifiedDomainHost_FallsBackToDefaultDomain()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Domains.Add(new Domain
        {
            Hostname = "unverified.example.com",
            IsVerified = false,
            VerificationToken = "tok-unverified",
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        // Host matches an unverified domain, so the default-domain code must
        // still resolve (host-based scoping only applies to verified domains).
        var client = _factory.CreateClientNoRedirect();
        var request = new HttpRequestMessage(HttpMethod.Get, "/default");
        request.Headers.Host = "unverified.example.com";

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("https://example.com/default", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task WellKnownVerificationFile_ServesTokenForRegisteredHost()
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/.well-known/shortnr-verify.txt");
        request.Headers.Host = "go.example.com";

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("tok-abc", body);
    }

    [Fact]
    public async Task WellKnownVerificationFile_UnregisteredHost_ReturnsNotFound()
    {
        var response = await _factory.CreateClient().GetAsync("/.well-known/shortnr-verify.txt");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
