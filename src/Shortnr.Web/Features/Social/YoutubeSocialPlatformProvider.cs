using Shortnr.Data.Entities;

namespace Shortnr.Web.Features.Social;

/// <summary>
/// YouTube (Google OAuth2) token refresh. Uses the standard Google token refresh endpoint.
/// </summary>
public sealed class YoutubeSocialPlatformProvider(
    IHttpClientFactory httpClientFactory,
    ILogger<YoutubeSocialPlatformProvider> logger) : ISocialPlatformProvider
{
    public string Platform => "youtube";

    public async Task<TokenRefreshResult?> RefreshTokenAsync(SocialAccount account, CancellationToken ct = default)
    {
        var refreshToken = account.RefreshTokenEncrypted;
        if (string.IsNullOrEmpty(refreshToken))
        {
            logger.LogWarning("YouTube account {AccountId} has no refresh token", account.PlatformAccountId);
            return null;
        }

        try
        {
            using var client = httpClientFactory.CreateClient("social-youtube");
            var response = await client.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(
            [
                new("client_id", "TODO"), // Inject from config
                new("client_secret", "TODO"),
                new("grant_type", "refresh_token"),
                new("refresh_token", refreshToken),
            ]), ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("YouTube token refresh failed for account {AccountId}: {StatusCode}",
                    account.PlatformAccountId, response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadFromJsonAsync<YoutubeTokenResponse>(ct);
            if (json is null)
                return null;

            return new TokenRefreshResult(
                AccessToken: json.AccessToken,
                RefreshToken: json.RefreshToken, // Google may or may not return a new refresh token
                AccessTokenExpiryUtc: DateTime.UtcNow.AddSeconds(json.ExpiresIn),
                RefreshTokenExpiryUtc: null); // Google refresh tokens don't expire (unless unused for 6 months)
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "YouTube token refresh failed for account {AccountId}", account.PlatformAccountId);
            return null;
        }
    }

    private sealed record YoutubeTokenResponse(
        string AccessToken,
        string? RefreshToken,
        int ExpiresIn);
}
