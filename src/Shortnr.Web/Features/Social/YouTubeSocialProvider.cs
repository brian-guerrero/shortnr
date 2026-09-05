using System.Net.Http.Json;
using System.Text.Json;
using Shortnr.Data.Entities;

namespace Shortnr.Web.Features.Social;

/// <summary>
/// YouTube social provider using the YouTube Data API v3 (PRD-021).
/// Requires a Google API key or OAuth2 credentials.
/// </summary>
public class YouTubeSocialProvider : ISocialPlatformProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<YouTubeSocialProvider> _logger;

    public SocialProvider Provider => SocialProvider.YouTube;
    public IReadOnlyList<string> RequiredScopes => ["https://www.googleapis.com/auth/youtube.readonly"];

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_config["Social:YouTube:ClientId"]) &&
        !string.IsNullOrWhiteSpace(_config["Social:YouTube:ClientSecret"]);

    public YouTubeSocialProvider(
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        ILogger<YouTubeSocialProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;
    }

    public string BuildAuthorizationUrl(string redirectUri, string state)
    {
        var clientId = _config["Social:YouTube:ClientId"] ?? string.Empty;
        var scopes = Uri.EscapeDataString(string.Join(" ", RequiredScopes));
        return $"https://accounts.google.com/o/oauth2/v2/auth?response_type=code" +
               $"&client_id={clientId}&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
               $"&scope={scopes}&state={state}&access_type=offline&prompt=consent";
    }

    public async Task<OAuthTokens?> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("SocialPlatforms");
            var clientId = _config["Social:YouTube:ClientId"] ?? string.Empty;
            var clientSecret = _config["Social:YouTube:ClientSecret"] ?? string.Empty;

            var response = await client.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(
            [
                new("grant_type", "authorization_code"),
                new("code", code),
                new("redirect_uri", redirectUri),
                new("client_id", clientId),
                new("client_secret", clientSecret)
            ]), ct);

            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            var accessToken = json.GetProperty("access_token").GetString() ?? string.Empty;
            var refreshToken = json.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
            var expiresIn = json.GetProperty("expires_in").GetInt32();

            // Fetch channel info
            var channelResponse = await client.GetFromJsonAsync<JsonElement>(
                $"https://www.googleapis.com/youtube/v3/channels?part=snippet,statistics&mine=true&access_token={accessToken}", ct);

            if (!channelResponse.TryGetProperty("items", out var items) || items.GetArrayLength() == 0)
                return null;

            var channel = items[0];
            var snippet = channel.GetProperty("snippet");
            var stats = channel.GetProperty("statistics");

            return new OAuthTokens
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresAtUtc = DateTime.UtcNow.AddSeconds(expiresIn),
                ExternalId = channel.GetProperty("id").GetString(),
                Username = snippet.TryGetProperty("title", out var title) ? title.GetString() : null,
                DisplayName = snippet.TryGetProperty("title", out var dn) ? dn.GetString() : null,
                AvatarUrl = snippet.TryGetProperty("thumbnails", out var thumbs) &&
                           thumbs.TryGetProperty("default", out var thumb) ?
                           thumb.TryGetProperty("url", out var thumbUrl) ? thumbUrl.GetString() : null : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "YouTube OAuth token exchange failed");
            return null;
        }
    }

    public async Task<SocialData?> FetchDataAsync(SocialAccount account, CancellationToken ct = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("SocialPlatforms");
            var token = account.AccessTokenEncrypted;

            // Fetch channel statistics
            var channelUrl = $"https://www.googleapis.com/youtube/v3/channels" +
                            $"?part=statistics,snippet&id={account.ExternalId}" +
                            $"&access_token={token}";

            var channelResponse = await client.GetFromJsonAsync<JsonElement>(channelUrl, ct);

            long? subscriberCount = null;
            var avatarUrl = account.AvatarUrl;

            if (channelResponse.TryGetProperty("items", out var items) && items.GetArrayLength() > 0)
            {
                var channel = items[0];
                if (channel.TryGetProperty("statistics", out var stats) &&
                    stats.TryGetProperty("subscriberCount", out var sc))
                {
                    subscriberCount = long.TryParse(sc.GetString(), out var scv) ? scv : null;
                }

                if (channel.TryGetProperty("snippet", out var snippet) &&
                    snippet.TryGetProperty("thumbnails", out var thumbs) &&
                    thumbs.TryGetProperty("default", out var thumb) &&
                    thumb.TryGetProperty("url", out var thumbUrl))
                {
                    avatarUrl = thumbUrl.GetString();
                }
            }

            // Fetch latest videos
            var videosUrl = $"https://www.googleapis.com/youtube/v3/search" +
                           $"?part=snippet&channelId={account.ExternalId}&order=date" +
                           $"&maxResults=3&type=video&access_token={token}";

            var videosResponse = await client.GetFromJsonAsync<JsonElement>(videosUrl, ct);
            var posts = new List<SocialPostItem>();

            if (videosResponse.TryGetProperty("items", out var videoItems))
            {
                foreach (var item in videoItems.EnumerateArray().Take(3))
                {
                    var snippet = item.GetProperty("snippet");
                    var videoId = item.GetProperty("id").TryGetProperty("videoId", out var vid) ?
                        vid.GetString() : item.GetProperty("id").GetString();

                    posts.Add(new SocialPostItem
                    {
                        ExternalPostId = videoId ?? "",
                        Title = snippet.TryGetProperty("title", out var t) ? t.GetString() : null,
                        Text = snippet.TryGetProperty("description", out var d) ? d.GetString()?.Truncate(256) : null,
                        MediaUrl = snippet.TryGetProperty("thumbnails", out var thumbs2) &&
                                  thumbs2.TryGetProperty("high", out var high) &&
                                  high.TryGetProperty("url", out var thumbUrl2) ? thumbUrl2.GetString() : null,
                        Permalink = $"https://youtube.com/watch?v={videoId}",
                        PublishedAtUtc = snippet.TryGetProperty("publishedAt", out var pa) ?
                            DateTime.TryParse(pa.GetString(), out var d2) ? d2 : null : null
                    });
                }
            }

            return new SocialData
            {
                Posts = posts,
                AudienceCount = subscriberCount,
                DisplayName = account.DisplayName,
                AvatarUrl = avatarUrl
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "YouTube data fetch failed for account {AccountId}", account.Id);
            return null;
        }
    }
}
