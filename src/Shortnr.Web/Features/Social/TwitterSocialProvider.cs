using System.Net.Http.Json;
using System.Text.Json;
using Shortnr.Data.Entities;

namespace Shortnr.Web.Features.Social;

/// <summary>
/// Twitter/X social provider using the Twitter API v2 (PRD-021).
/// Free tier: 1,500 tweets/month read limit — aggressive caching is essential.
/// </summary>
public class TwitterSocialProvider : ISocialPlatformProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<TwitterSocialProvider> _logger;

    public SocialProvider Provider => SocialProvider.Twitter;
    public IReadOnlyList<string> RequiredScopes => ["tweet.read", "users.read", "offline.access"];

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_config["Social:Twitter:ClientId"]) &&
        !string.IsNullOrWhiteSpace(_config["Social:Twitter:ClientSecret"]);

    public TwitterSocialProvider(
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        ILogger<TwitterSocialProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;
    }

    public string BuildAuthorizationUrl(string redirectUri, string state)
    {
        var clientId = _config["Social:Twitter:ClientId"] ?? string.Empty;
        var scopes = string.Join("%20", RequiredScopes);
        return $"https://twitter.com/i/oauth2/authorize?response_type=code&client_id={clientId}" +
               $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
               $"&scope={scopes}&state={state}&code_challenge=plain&code_challenge_method=plain";
    }

    public async Task<OAuthTokens?> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("SocialPlatforms");
            var clientId = _config["Social:Twitter:ClientId"] ?? string.Empty;
            var clientSecret = _config["Social:Twitter:ClientSecret"] ?? string.Empty;

            var response = await client.PostAsync("https://api.twitter.com/2/oauth2/token", new FormUrlEncodedContent(
            [
                new("grant_type", "authorization_code"),
                new("code", code),
                new("redirect_uri", redirectUri),
                new("client_id", clientId),
                new("code_verifier", "plain")
            ]), ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Twitter token exchange failed: {Status}", response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            var accessToken = json.GetProperty("access_token").GetString() ?? string.Empty;
            var refreshToken = json.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
            var expiresIn = json.GetProperty("expires_in").GetInt32();

            // Fetch user profile
            var userClient = _httpClientFactory.CreateClient("SocialPlatforms");
            userClient.DefaultRequestHeaders.Authorization = new("Bearer", accessToken);
            var userResponse = await userClient.GetAsync("https://api.twitter.com/2/users/me?user.fields=profile_image_url,name,username,public_metrics", ct);

            if (!userResponse.IsSuccessStatusCode) return null;

            var userJson = await userResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
            var userData = userJson.GetProperty("data");

            return new OAuthTokens
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresAtUtc = DateTime.UtcNow.AddSeconds(expiresIn),
                ExternalId = userData.GetProperty("id").GetString(),
                Username = userData.GetProperty("username").GetString(),
                DisplayName = userData.GetProperty("name").GetString(),
                AvatarUrl = userData.TryGetProperty("profile_image_url", out var img) ? img.GetString() : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Twitter OAuth token exchange failed");
            return null;
        }
    }

    public async Task<SocialData?> FetchDataAsync(SocialAccount account, CancellationToken ct = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("SocialPlatforms");
            var token = await RefreshTokenIfNeededAsync(account, client, ct);
            if (token is null) return null;

            client.DefaultRequestHeaders.Authorization = new("Bearer", token);

            // Fetch latest tweets
            var tweetsUrl = $"https://api.twitter.com/2/users/{account.ExternalId}/tweets" +
                           $"?max_results=3&tweet.fields=created_at,text,entities" +
                           $"&expansions=attachments.media_keys&media.fields=preview_image_url,url";

            var tweetsResponse = await client.GetAsync(tweetsUrl, ct);
            if (!tweetsResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("Twitter tweets fetch failed: {Status}", tweetsResponse.StatusCode);
                return null;
            }

            var tweetsJson = await tweetsResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
            var posts = new List<SocialPostItem>();

            if (tweetsJson.TryGetProperty("data", out var tweetsData))
            {
                var mediaMap = new Dictionary<string, string>();
                if (tweetsJson.TryGetProperty("includes", out var includes) &&
                    includes.TryGetProperty("media", out var media))
                {
                    foreach (var m in media.EnumerateArray())
                    {
                        var key = m.GetProperty("media_key").GetString() ?? "";
                        var url = m.TryGetProperty("url", out var u) ? u.GetString() :
                                  m.TryGetProperty("preview_image_url", out var p) ? p.GetString() : null;
                        if (url is not null) mediaMap[key] = url;
                    }
                }

                foreach (var tweet in tweetsData.EnumerateArray().Take(3))
                {
                    var tweetId = tweet.GetProperty("id").GetString() ?? "";
                    var text = tweet.GetProperty("text").GetString() ?? "";
                    string? mediaUrl = null;

                    if (tweet.TryGetProperty("attachments", out var attachments) &&
                        attachments.TryGetProperty("media_keys", out var keys) &&
                        keys.EnumerateArray().FirstOrDefault() is var firstKey &&
                        firstKey.ValueKind != JsonValueKind.Undefined)
                    {
                        var key = firstKey.GetString() ?? "";
                        mediaMap.TryGetValue(key, out mediaUrl);
                    }

                    posts.Add(new SocialPostItem
                    {
                        ExternalPostId = tweetId,
                        Text = text,
                        Title = text.Length > 80 ? text[..80] + "…" : text,
                        MediaUrl = mediaUrl,
                        Permalink = $"https://x.com/{account.Username}/status/{tweetId}",
                        PublishedAtUtc = tweet.TryGetProperty("created_at", out var cat) ?
                            DateTime.TryParse(cat.GetString(), out var d) ? d : null : null
                    });
                }
            }

            // Fetch follower count
            var userResponse = await client.GetAsync(
                $"https://api.twitter.com/2/users/{account.ExternalId}?user.fields=public_metrics", ct);

            long? followerCount = null;
            if (userResponse.IsSuccessStatusCode)
            {
                var userJson = await userResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
                if (userJson.TryGetProperty("data", out var ud) &&
                    ud.TryGetProperty("public_metrics", out var metrics) &&
                    metrics.TryGetProperty("followers_count", out var fc))
                {
                    followerCount = fc.GetInt64();
                }
            }

            return new SocialData
            {
                Posts = posts,
                AudienceCount = followerCount,
                DisplayName = account.DisplayName,
                AvatarUrl = account.AvatarUrl
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Twitter data fetch failed for account {AccountId}", account.Id);
            return null;
        }
    }

    private async Task<string?> RefreshTokenIfNeededAsync(SocialAccount account, HttpClient client, CancellationToken ct)
    {
        if (account.TokenExpiresUtc.HasValue && account.TokenExpiresUtc.Value > DateTime.UtcNow.AddMinutes(5))
            return account.AccessTokenEncrypted; // Already valid (stored encrypted, but we pass through)

        if (string.IsNullOrEmpty(account.RefreshTokenEncrypted))
            return null;

        try
        {
            var clientId = _config["Social:Twitter:ClientId"] ?? string.Empty;
            var clientSecret = _config["Social:Twitter:ClientSecret"] ?? string.Empty;

            var response = await client.PostAsync("https://api.twitter.com/2/oauth2/token", new FormUrlEncodedContent(
            [
                new("grant_type", "refresh_token"),
                new("refresh_token", account.RefreshTokenEncrypted),
                new("client_id", clientId)
            ]), ct);

            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            var accessToken = json.GetProperty("access_token").GetString() ?? string.Empty;

            // Update the account with new tokens (caller should save)
            account.AccessTokenEncrypted = accessToken;
            if (json.TryGetProperty("refresh_token", out var newRt))
                account.RefreshTokenEncrypted = newRt.GetString();
            if (json.TryGetProperty("expires_in", out var exp))
                account.TokenExpiresUtc = DateTime.UtcNow.AddSeconds(exp.GetInt32());

            return accessToken;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Twitter token refresh failed for account {AccountId}", account.Id);
            return null;
        }
    }
}
