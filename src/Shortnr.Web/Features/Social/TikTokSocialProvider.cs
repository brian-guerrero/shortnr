using System.Net.Http.Json;
using System.Text.Json;
using Shortnr.Data.Entities;

namespace Shortnr.Web.Features.Social;

/// <summary>
/// TikTok social provider using the TikTok Content Posting API (PRD-021).
/// Custom OAuth implementation since AspNet.Security.OAuth.Providers doesn't
/// include TikTok. Uses the TikTok for Developers API v2.
/// </summary>
public class TikTokSocialProvider : ISocialPlatformProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<TikTokSocialProvider> _logger;

    public SocialProvider Provider => SocialProvider.TikTok;
    public IReadOnlyList<string> RequiredScopes => ["user.info.basic", "video.list"];

    public TikTokSocialProvider(
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        ILogger<TikTokSocialProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;
    }

    public string BuildAuthorizationUrl(string redirectUri, string state)
    {
        var clientId = _config["Social:TikTok:ClientId"] ?? string.Empty;
        var scopes = string.Join(",", RequiredScopes);
        return $"https://www.tiktok.com/v2/auth/authorize/" +
               $"?client_key={clientId}" +
               $"&scope={scopes}" +
               $"&response_type=code" +
               $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
               $"&state={state}";
    }

    public async Task<OAuthTokens?> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("SocialPlatforms");
            var clientId = _config["Social:TikTok:ClientId"] ?? string.Empty;
            var clientSecret = _config["Social:TikTok:ClientSecret"] ?? string.Empty;

            var response = await client.PostAsJsonAsync("https://open.tiktokapis.com/v2/oauth/token/", new
            {
                client_key = clientId,
                client_secret = clientSecret,
                code,
                grant_type = "authorization_code",
                redirect_uri = redirectUri
            }, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("TikTok token exchange failed: {Status}", response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            var data = json.GetProperty("data");
            var accessToken = data.GetProperty("access_token").GetString() ?? string.Empty;
            var refreshToken = data.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
            var expiresIn = data.GetProperty("expires_in").GetInt32();
            var openId = data.GetProperty("open_id").GetString() ?? string.Empty;

            // Fetch user info
            var userResponse = await client.GetFromJsonAsync<JsonElement>(
                $"https://open.tiktokapis.com/v2/user/info/?fields=display_name,avatar_url,follower_count" +
                $"&access_token={accessToken}", ct);

            var userInfo = userResponse.TryGetProperty("data", out var userData) &&
                          userData.TryGetProperty("user", out var user) ? user : default;

            return new OAuthTokens
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresAtUtc = DateTime.UtcNow.AddSeconds(expiresIn),
                ExternalId = openId,
                Username = userInfo.ValueKind != JsonValueKind.Undefined ?
                    userInfo.TryGetProperty("display_name", out var dn) ? dn.GetString() : null : null,
                DisplayName = userInfo.ValueKind != JsonValueKind.Undefined ?
                    userInfo.TryGetProperty("display_name", out var dn2) ? dn2.GetString() : null : null,
                AvatarUrl = userInfo.ValueKind != JsonValueKind.Undefined ?
                    userInfo.TryGetProperty("avatar_url", out var av) ? av.GetString() : null : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TikTok OAuth token exchange failed");
            return null;
        }
    }

    public async Task<SocialData?> FetchDataAsync(SocialAccount account, CancellationToken ct = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("SocialPlatforms");
            var token = account.AccessTokenEncrypted;

            // Fetch user info with follower count
            var userResponse = await client.GetFromJsonAsync<JsonElement>(
                $"https://open.tiktokapis.com/v2/user/info/?fields=display_name,avatar_url,follower_count" +
                $"&access_token={token}", ct);

            long? followerCount = null;
            var avatarUrl = account.AvatarUrl;

            if (userResponse.TryGetProperty("data", out var userData) &&
                userData.TryGetProperty("user", out var user))
            {
                followerCount = user.TryGetProperty("follower_count", out var fc) ? fc.GetInt64() : null;
                avatarUrl = user.TryGetProperty("avatar_url", out var av) ? av.GetString() : avatarUrl;
            }

            // Fetch latest videos
            var videosResponse = await client.GetFromJsonAsync<JsonElement>(
                $"https://open.tiktokapis.com/v2/video/list/?fields=id,title,cover_image_url,create_time,share_url" +
                $"&access_token={token}", ct);

            var posts = new List<SocialPostItem>();

            if (videosResponse.TryGetProperty("data", out var videoData) &&
                videoData.TryGetProperty("videos", out var videos))
            {
                foreach (var video in videos.EnumerateArray().Take(3))
                {
                    var videoId = video.GetProperty("id").GetString() ?? "";
                    posts.Add(new SocialPostItem
                    {
                        ExternalPostId = videoId,
                        Title = video.TryGetProperty("title", out var t) ? t.GetString() : null,
                        Text = video.TryGetProperty("title", out var tt) ? tt.GetString() : null,
                        MediaUrl = video.TryGetProperty("cover_image_url", out var c) ? c.GetString() : null,
                        Permalink = video.TryGetProperty("share_url", out var su) ? su.GetString() :
                                   $"https://tiktok.com/@{account.Username}/video/{videoId}",
                        PublishedAtUtc = video.TryGetProperty("create_time", out var ct2) ?
                            ct2.GetInt64() is var ts && ts > 0 ? DateTimeOffset.FromUnixTimeSeconds(ts).UtcDateTime : null : null
                    });
                }
            }

            return new SocialData
            {
                Posts = posts,
                AudienceCount = followerCount,
                DisplayName = account.DisplayName,
                AvatarUrl = avatarUrl
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TikTok data fetch failed for account {AccountId}", account.Id);
            return null;
        }
    }
}
