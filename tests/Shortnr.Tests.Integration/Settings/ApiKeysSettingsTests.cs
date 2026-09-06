using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Tests.Integration.Infrastructure;

namespace Shortnr.Tests.Integration.Settings;

/// <summary>
/// Verifies the /settings/api-keys page: key creation (plaintext shown once),
/// revocation, owner scoping and auth gating.
/// </summary>
public class ApiKeysSettingsTests : IntegrationTestBase
{

    [Fact]
    public async Task CreateKey_ShowsPlaintextOnce_AndStoresOnlyHash()
    {
        Factory.Services.GetRequiredService<TestAuthState>().SetAuthenticatedUser("alice", ShortnrWebAppFactory.TestIssuer);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Users.Add(new User
            {
                Issuer = ShortnrWebAppFactory.TestIssuer,
                Subject = "alice",
                CreatedAtUtc = DateTime.UtcNow,
                LastLoginAtUtc = DateTime.UtcNow
            });
            db.SaveChanges();
        }

        var client = Factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/settings/api-keys", token,
            ("label", "CI pipeline"),
            ("scope", ApiKeyScopes.LinksRead),
            ("scope", ApiKeyScopes.LinksWrite));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();

        // Extract the shown plaintext key (snr_ prefix + 64 hex chars).
        var match = Regex.Match(body, @"(snr_[0-9a-f]{64})");
        Assert.True(match.Success, "Plaintext API key not shown.");
        var plaintext = match.Groups[1].Value;
        Assert.Contains("shown only once", body);

        using var scope2 = Factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var key = await db2.ApiKeys.SingleAsync();
        Assert.Equal("CI pipeline", key.Label);
        Assert.Null(key.RevokedAt);
        Assert.StartsWith("snr_", key.KeyPrefix);

        // Only the SHA-256 hash is persisted, never the plaintext.
        Assert.Equal(ApiKeyService.HashKey(plaintext), key.KeyHash);
        Assert.NotEqual(plaintext, key.KeyHash);
        Assert.DoesNotContain(plaintext, key.KeyHash);
        Assert.Equal(64, key.KeyHash.Length);
    }

    [Fact]
    public async Task RevokeKey_MarksItRevoked()
    {
        Factory.Services.GetRequiredService<TestAuthState>().SetAuthenticatedUser("alice", ShortnrWebAppFactory.TestIssuer);
        var ownerUserId = await SeedUserAndKeyAsync("alice", "snr_existingkey0000000000000000000000");

        var client = Factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/settings/api-keys?handler=Revoke", token,
            ("id", (await FindKeyIdAsync(ownerUserId)).ToString()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("revoked", body);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.NotNull((await db.ApiKeys.SingleAsync(k => k.OwnerUserId == ownerUserId)).RevokedAt);
    }

    [Fact]
    public async Task ApiKeysAreScopedToOwner()
    {
        Factory.Services.GetRequiredService<TestAuthState>().SetAuthenticatedUser("alice", ShortnrWebAppFactory.TestIssuer);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var alice = new User { Issuer = ShortnrWebAppFactory.TestIssuer, Subject = "alice", CreatedAtUtc = DateTime.UtcNow, LastLoginAtUtc = DateTime.UtcNow };
            var bob = new User { Issuer = ShortnrWebAppFactory.TestIssuer, Subject = "bob", CreatedAtUtc = DateTime.UtcNow, LastLoginAtUtc = DateTime.UtcNow };
            db.Users.AddRange(alice, bob);
            db.SaveChanges();

            db.ApiKeys.Add(new ApiKey
            {
                OwnerUserId = bob.Id,
                KeyHash = ApiKeyService.HashKey("snr_bobkey000000000000000000000000000"),
                KeyPrefix = "snr_",
                Label = "bob key",
                CreatedAtUtc = DateTime.UtcNow
            });
            db.SaveChanges();
        }

        var response = await Factory.CreateClient().GetAsync("/settings/api-keys");
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("bob key", body);
    }

    [Fact]
    public async Task WhenAuthEnabled_UnauthenticatedFullPage_RedirectsToIndex()
    {
        using var authFactory = new ShortnrWebAppFactory(authEnabled: true);
        var client = authFactory.CreateClientNoRedirect();

        var response = await client.GetAsync("/settings/api-keys");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task WhenAuthEnabled_UnauthenticatedHtmx_ReturnsUnauthorized()
    {
        using var authFactory = new ShortnrWebAppFactory(authEnabled: true);
        var client = authFactory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/settings/api-keys");
        request.Headers.Add("HX-Request", "true");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<long> SeedUserAndKeyAsync(string subject, string key)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new User
        {
            Issuer = ShortnrWebAppFactory.TestIssuer,
            Subject = subject,
            CreatedAtUtc = DateTime.UtcNow,
            LastLoginAtUtc = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.ApiKeys.Add(new ApiKey
        {
            OwnerUserId = user.Id,
            KeyHash = ApiKeyService.HashKey(key),
            KeyPrefix = "snr_",
            Label = "existing key",
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return user.Id;
    }

    private async Task<long> FindKeyIdAsync(long ownerUserId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.ApiKeys.SingleAsync(k => k.OwnerUserId == ownerUserId)).Id;
    }

    private static Task<string> GetAntiforgeryTokenAsync(HttpClient client) =>
        TestForms.GetAntiforgeryTokenAsync(client, "/settings/api-keys");

    private static Task<HttpResponseMessage> PostFormAsync(HttpClient client, string path, string token, params (string Name, string Value)[] fields) =>
        TestForms.PostFormAsync(client, path, token, fields);
}
