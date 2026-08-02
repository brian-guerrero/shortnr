using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Tests.Integration.Infrastructure;
using Shortnr.Web.Models;
using Shortnr.Web.Services;

namespace Shortnr.Tests.Integration.Api;

/// <summary>
/// Verifies API-key scoping: read/write separation on /api/v1, mcp:* keys excluded
/// from the REST API, legacy keys with empty scopes keeping full access, and the
/// scope picker on the settings page.
/// </summary>
public class ApiKeyScopesTests : IAsyncLifetime
{
    private const string TestKey = "snr_testkey1234567890abcdef1234567890";
    private static readonly Regex AntiforgeryTokenRegex = new(
        @"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""",
        RegexOptions.Compiled);

    private readonly ShortnrWebAppFactory _factory = new(authEnabled: true);

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    [Fact]
    public async Task ReadOnlyKey_CanListButCannotCreate()
    {
        await SeedUserAndKeyAsync("read-only", TestKey, ApiKeyScopes.LinksRead);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestKey);

        var list = await client.GetAsync("/api/v1/links");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);

        var create = await client.PostAsJsonAsync("/api/v1/links", new CreateLinkRequest { Url = "https://example.com/x" });
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

    [Fact]
    public async Task WriteOnlyKey_CanCreateButCannotList()
    {
        await SeedUserAndKeyAsync("write-only", TestKey, ApiKeyScopes.LinksWrite);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestKey);

        var create = await client.PostAsJsonAsync("/api/v1/links", new CreateLinkRequest { Url = "https://example.com/x" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var list = await client.GetAsync("/api/v1/links");
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
    }

    [Fact]
    public async Task McpOnlyKey_CannotUseRestApi()
    {
        await SeedUserAndKeyAsync("mcp-only", TestKey, ApiKeyScopes.McpRead);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestKey);

        var list = await client.GetAsync("/api/v1/links");
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);

        var create = await client.PostAsJsonAsync("/api/v1/links", new CreateLinkRequest { Url = "https://example.com/x" });
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

    [Fact]
    public async Task LegacyKeyWithEmptyScopes_HasFullAccess()
    {
        await SeedUserAndKeyAsync("legacy", TestKey, "");
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestKey);

        var create = await client.PostAsJsonAsync("/api/v1/links", new CreateLinkRequest { Url = "https://example.com/legacy" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var list = await client.GetAsync("/api/v1/links");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
    }

    [Fact]
    public async Task SettingsPage_StoresSelectedScopes()
    {
        _factory.Services.GetRequiredService<TestAuthState>().SetAuthenticatedUser("alice", ShortnrWebAppFactory.TestIssuer);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Users.Add(new User
            {
                Issuer = ShortnrWebAppFactory.TestIssuer,
                Subject = "alice",
                CreatedAtUtc = DateTime.UtcNow,
                LastLoginAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/settings/api-keys", token,
            ("label", "mcp key"),
            ("scope", ApiKeyScopes.McpRead),
            ("scope", ApiKeyScopes.McpWrite));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope2 = _factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var key = await db2.ApiKeys.SingleAsync(k => k.Label == "mcp key");
        Assert.Equal("mcp:read mcp:write", key.Scopes);
    }

    [Fact]
    public async Task SettingsPage_NoScopeSelected_ShowsError()
    {
        _factory.Services.GetRequiredService<TestAuthState>().SetAuthenticatedUser("alice", ShortnrWebAppFactory.TestIssuer);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Users.Add(new User
            {
                Issuer = ShortnrWebAppFactory.TestIssuer,
                Subject = "alice",
                CreatedAtUtc = DateTime.UtcNow,
                LastLoginAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/settings/api-keys", token, ("label", "scopeless"));

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Select at least one valid scope", body);
    }

    private async Task<long> SeedUserAndKeyAsync(string subject, string key, string scopes)
    {
        using var scope = _factory.Services.CreateScope();
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
            Label = "test key",
            Scopes = scopes,
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return user.Id;
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        var page = await client.GetAsync("/settings/api-keys");
        var html = await page.Content.ReadAsStringAsync();
        var match = AntiforgeryTokenRegex.Match(html);
        Assert.True(match.Success, "Antiforgery token not found in api-keys page.");
        return match.Groups[1].Value;
    }

    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string path, string token, params (string Name, string Value)[] fields)
    {
        var pairs = fields
            .Select(f => new KeyValuePair<string, string>(f.Name, f.Value))
            .ToList();
        pairs.Add(new KeyValuePair<string, string>("__RequestVerificationToken", token));
        return await client.PostAsync(path, new FormUrlEncodedContent(pairs));
    }
}
