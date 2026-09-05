using System.Net.Http.Json;
using System.Text.Json;
using Shortnr.Data.Entities;

namespace Shortnr.Web.Features.Social;

/// <summary>
/// Instagram social provider using the Instagram Graph API (PRD-021).
/// Requires a Facebook App and Instagram Business/Creator account.
/// </summary>
public class InstagramSocialProvider : ISocialPlatformProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<InstagramSocialProvider> _logger;

    public SocialProvider Provider => SocialProvider.Instagram;
    public IReadOnlyList<string> RequiredScopes => ["user_profile", "user_media"];

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_config["Social:Instagram:ClientId"]) &&
        !string.IsNullOrWhiteSpace(_config["Social:Instagram:ClientSecret"]);

    public InstagramSocialProvider(
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        ILogger<InstagramSocialProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;
    }

    public string BuildAuthorizationUrl(string redirectUri, string state)
    {
        var clientId = _config["Social:Instagram:ClientId"] ?? string.Empty;
        var scopes = string.Join(",", RequiredScopes);
        return $"https://www.facebook.com/v19.0/dialog/oauth?client_id={clientId}" +
               $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
               $"&scope={scopes}&state={state}&response_type=code";
    }

    public async Task<OAuthTokens?> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("SocialPlatforms");
            var clientId = _config["Social:Instagram:ClientId"] ?? string.Empty;
            var clientSecret = _config["Social:Instagram:ClientSecret"] ?? string.Empty;

            // Exchange code for short-lived token
            var tokenUrl = $"https://graph.facebook.com/v19.0/oauth/access_token" +
                          $"?client_id={clientId}&client_secret={clientSecret}" +
                          $"&redirect_uri={Uri.EscapeDataString(redirectUri)}&code={code}";

            var tokenResponse = await client.GetFromJsonAsync<JsonElement>(tokenUrl, ct);
            var shortToken = tokenResponse.GetProperty("access_token").GetString() ?? string.Empty;

            // Exchange for long-lived token
            var longTokenUrl = $"https://graph.facebook.com/v19.0/oauth/access_token" +
                              $"?grant_type=fb_exchange_token&client_id={clientId}" +
                              $"&client_secret={clientSecret}&fb_exchange_token={shortToken}";

            var longTokenResponse = await client.GetFromJsonAsync<JsonElement>(longTokenUrl, ct);
            var accessToken = longTokenResponse.GetProperty("access_token").GetString() ?? string.Empty;

            // Fetch user profile
            var userResponse = await client.GetFromJsonAsync<JsonElement>(
                $"https://graph.facebook.com/v19.0/me?fields=id,username,name&access_token={accessToken}", ct);

            return new OAuthTokens
            {
                AccessToken = accessToken,
                ExternalId = userResponse.GetProperty("id").GetString(),
                Username = userResponse.GetProperty("username").GetString(),
                DisplayName = userResponse.TryGetProperty("name", out var n) ? n.GetString() : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Instagram OAuth token exchange failed");
            return null;
        }
    }

    public async Task<SocialData?> FetchDataAsync(SocialAccount account, CancellationToken ct = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("SocialPlatforms");
            var token = account.AccessTokenEncrypted;

            // Fetch user profile with follower count
            var profileUrl = $"https://graph.facebook.com/v19.0/{account.ExternalId}" +
                            $"?fields=username,name,followers_count,profile_picture_url" +
                            $"&access_token={token}";

            var profileResponse = await client.GetFromJsonAsync<JsonElement>(profileUrl, ct);

            var followerCount = profileResponse.TryGetProperty("followers_count", out var fc) ? fc.GetInt64() : (long?)null;
            var avatarUrl = profileResponse.TryGetProperty("profile_picture_url", out var av) ? av.GetString() : account.AvatarUrl;

            // Fetch latest media
            var mediaUrl = $"https://graph.facebook.com/v19.0/{account.ExternalId}/media" +
                          $"?fields=id,caption,media_type,media_url,permalink,timestamp" +
                          $"&limit=3&access_token={token}";

            var mediaResponse = await client.GetFromJsonAsync<JsonElement>(mediaUrl, ct);
            var posts = new List<SocialPostItem>();

            if (mediaResponse.TryGetProperty("data", out var mediaData))
            {
                foreach (var item in mediaData.EnumerateArray().Take(3))
                {
                    var mediaType = item.TryGetProperty("media_type", out var mt) ? mt.GetString() : "IMAGE";
                    posts.Add(new SocialPostItem
                    {
                        ExternalPostId = item.GetProperty("id").GetString() ?? "",
                        Title = item.TryGetProperty("caption", out var cap) ? cap.GetString()?.Split('\n').FirstOrDefault()?.Truncate(256) : null,
                        Text = item.TryGetProperty("caption", out var t) ? t.GetString() : null,
                        MediaUrl = item.TryGetProperty("media_url", out var mu) ? mu.GetString() : null,
                        Permalink = item.TryGetProperty("permalink", out var pl) ? pl.GetString() : null,
                        PublishedAtUtc = item.TryGetProperty("timestamp", out var ts) ?
                            DateTime.TryParse(ts.GetString(), out var d) ? d : null : null
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
            _logger.LogError(ex, "Instagram data fetch failed for account {AccountId}", account.Id);
            return null;
        }
    }
}

internal static class StringExtensions
{
    internal static string? Truncate(this string? value, int maxLength) =>
        value is null ? null : value.Length <= maxLength ? value : value[..maxLength] + "…";
}
