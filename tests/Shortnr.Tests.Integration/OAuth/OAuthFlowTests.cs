using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Shortnr.Tests.Integration.Infrastructure;
using Shortnr.Web.Services;

namespace Shortnr.Tests.Integration.OAuth;

/// <summary>
/// Exercises the in-process OAuth 2.1 authorization server fronting the OIDC
/// login: the RFC 9728 protected-resource metadata document, RFC 7591 dynamic
/// client registration, and the full authorization-code + PKCE flow through to
/// an authenticated /mcp call. The Dex leg of the authorize flow is replaced by
/// <see cref="TestAuthHandler"/> (controlled via <see cref="TestAuthState"/>);
/// OpenIddict itself still validates client, redirect URI, PKCE and scopes.
/// </summary>
public class OAuthFlowTests : IAsyncLifetime
{
    private const string LoopbackRedirectUri = "http://127.0.0.1:9999/callback";
    private const string Resource = "http://localhost:5156/mcp";

    private readonly ShortnrWebAppFactory _factory = new(authEnabled: true);

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    [Fact]
    public async Task ProtectedResourceMetadata_ExposesMcpResource()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/.well-known/oauth-protected-resource/mcp");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = json.RootElement;

        Assert.EndsWith("/mcp", root.GetProperty("resource").GetString());
        Assert.Contains("http://localhost:5156",
            root.GetProperty("authorization_servers").EnumerateArray().Select(e => e.GetString()));
        var scopes = root.GetProperty("scopes_supported").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains(ApiKeyScopes.McpRead, scopes);
        Assert.Contains(ApiKeyScopes.McpWrite, scopes);
    }

    [Fact]
    public async Task AuthorizationServerMetadata_AdvertisesRegistrationEndpoint()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/.well-known/openid-configuration");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.EndsWith("/connect/register", json.RootElement.GetProperty("registration_endpoint").GetString());
    }

    [Fact]
    public async Task McpEndpoint_NoAuth_ChallengesWithProtectedResourceMetadata()
    {
        var client = _factory.CreateClient();

        var response = await PostJsonRpcAsync(client, "tools/list", "{}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var challenges = response.Headers.GetValues("WWW-Authenticate");
        Assert.Contains(challenges, h => h.Contains("resource_metadata=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RegisterClient_Returns201WithClientId()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/connect/register", new
        {
            client_name = "opencode-test",
            redirect_uris = new[] { LoopbackRedirectUri },
            grant_types = new[] { "authorization_code", "refresh_token" }
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.False(string.IsNullOrEmpty(json.RootElement.GetProperty("client_id").GetString()));
    }

    [Fact]
    public async Task RegisterClient_RejectsNonLoopbackHttpRedirect()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/connect/register", new
        {
            client_name = "bad-client",
            redirect_uris = new[] { "http://example.com/callback" }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RegisterClient_RejectsUnknownGrantType()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/connect/register", new
        {
            client_name = "bad-client",
            redirect_uris = new[] { LoopbackRedirectUri },
            grant_types = new[] { "client_credentials" }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FullPkceFlow_AuthorizeThenToken_ThenCallMcp()
    {
        var clientId = await RegisterClientAsync();
        var (verifier, challenge) = CreatePkcePair();

        // Browser leg: the user is already signed in via Dex (here: TestAuthHandler).
        var authState = _factory.Services.GetRequiredService<TestAuthState>();
        authState.SetAuthenticatedUser("oauth-user", ShortnrWebAppFactory.TestIssuer, name: "OAuth User");

        var code = await GetAuthorizationCodeAsync(clientId, challenge, ApiKeyScopes.McpRead);
        var accessToken = await ExchangeCodeAsync(clientId, code, verifier);

        var mcpClient = _factory.CreateClient();
        mcpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await PostJsonRpcAsync(mcpClient, "tools/list", "{}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ReadJsonAsync(response);
        var tools = json.RootElement.GetProperty("result").GetProperty("tools");
        Assert.Contains("ping", tools.EnumerateArray().Select(t => t.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task RefreshTokenGrant_IssuesNewAccessToken()
    {
        var clientId = await RegisterClientAsync();
        var (verifier, challenge) = CreatePkcePair();

        var authState = _factory.Services.GetRequiredService<TestAuthState>();
        authState.SetAuthenticatedUser("refresh-user", ShortnrWebAppFactory.TestIssuer, name: "Refresh User");

        var code = await GetAuthorizationCodeAsync(clientId, challenge, $"{ApiKeyScopes.McpRead} offline_access");
        var tokenResponse = await ExchangeCodeFullAsync(clientId, code, verifier);
        using var tokenJson = await JsonDocument.ParseAsync(await tokenResponse.Content.ReadAsStreamAsync());
        var refreshToken = tokenJson.RootElement.GetProperty("refresh_token").GetString();
        Assert.False(string.IsNullOrEmpty(refreshToken));

        var client = _factory.CreateClient();
        var refresh = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken!,
            ["client_id"] = clientId
        }));

        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        using var refreshJson = await JsonDocument.ParseAsync(await refresh.Content.ReadAsStreamAsync());
        Assert.False(string.IsNullOrEmpty(refreshJson.RootElement.GetProperty("access_token").GetString()));
    }

    [Fact]
    public async Task Authorize_WithoutLogin_RedirectsToOidc()
    {
        var clientId = await RegisterClientAsync();
        var (_, challenge) = CreatePkcePair();

        // No TestAuthState set: the browser leg must redirect to Dex.
        var client = _factory.CreateClientNoRedirect();

        var response = await client.GetAsync(AuthorizeUrl(clientId, challenge, ApiKeyScopes.McpRead));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith($"{ShortnrWebAppFactory.TestIssuer}/authorize", response.Headers.Location!.AbsoluteUri);
    }

    private async Task<long> SeedUserAsync(string subject)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Shortnr.Data.AppDbContext>();
        var user = new Shortnr.Data.Entities.User
        {
            Issuer = ShortnrWebAppFactory.TestIssuer,
            Subject = subject,
            CreatedAtUtc = DateTime.UtcNow,
            LastLoginAtUtc = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private async Task<string> RegisterClientAsync()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/connect/register", new
        {
            client_name = "opencode-test",
            redirect_uris = new[] { LoopbackRedirectUri },
            grant_types = new[] { "authorization_code", "refresh_token" }
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return json.RootElement.GetProperty("client_id").GetString()!;
    }

    private static string AuthorizeUrl(string clientId, string challenge, string scope) =>
        $"/connect/authorize?response_type=code&client_id={clientId}" +
        $"&redirect_uri={Uri.EscapeDataString(LoopbackRedirectUri)}" +
        $"&scope={Uri.EscapeDataString(scope)}" +
        $"&resource={Uri.EscapeDataString(Resource)}" +
        $"&code_challenge={challenge}&code_challenge_method=S256";

    private async Task<string> GetAuthorizationCodeAsync(string clientId, string challenge, string scope)
    {
        var client = _factory.CreateClientNoRedirect();
        var response = await client.GetAsync(AuthorizeUrl(clientId, challenge, scope));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        Assert.StartsWith(LoopbackRedirectUri, location.GetLeftPart(UriPartial.Path));

        var query = QueryHelpers.ParseQuery(location.Query);
        Assert.True(query.TryGetValue("code", out var code), "Authorize response did not carry a code.");
        return code.ToString();
    }

    private async Task<string> ExchangeCodeAsync(string clientId, string code, string verifier)
    {
        var response = await ExchangeCodeFullAsync(clientId, code, verifier);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return json.RootElement.GetProperty("access_token").GetString()!;
    }

    private async Task<HttpResponseMessage> ExchangeCodeFullAsync(string clientId, string code, string verifier)
    {
        var client = _factory.CreateClient();
        return await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = LoopbackRedirectUri,
            ["client_id"] = clientId,
            ["code_verifier"] = verifier
        }));
    }

    private static (string Verifier, string Challenge) CreatePkcePair()
    {
        var verifierBytes = RandomNumberGenerator.GetBytes(32);
        var verifier = Base64UrlEncode(verifierBytes);
        var challenge = Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return (verifier, challenge);
    }

    private static string Base64UrlEncode(byte[] input) =>
        Convert.ToBase64String(input).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private const string ProtocolVersion = "2025-06-18";

    private static async Task<HttpResponseMessage> PostJsonRpcAsync(HttpClient client, string method, string @params)
    {
        var body = $$"""{"jsonrpc":"2.0","id":1,"method":"{{method}}","params":{{@params}}}""";
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", ProtocolVersion);
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
        return await client.SendAsync(request);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        var payload = string.Join("", text.Split('\n')
            .Where(line => line.StartsWith("data:", StringComparison.Ordinal))
            .Select(line => line["data:".Length..].Trim()));
        return JsonDocument.Parse(payload.Length > 0 ? payload : text);
    }
}
