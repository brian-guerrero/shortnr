using Shortnr.Data.Entities;

namespace Shortnr.Web.Features.Social;

/// <summary>
/// TikTok Content Posting API token refresh. Uses the OAuth 2.0 refresh token endpoint.
/// </summary>
public sealed class TiktokSocialPlatformProvider(
    IHttpClientFactory httpClientFactory,
    ILogger<TiktokSocialPlatformProvider> logger) : ISocialPlatformProvider
{
    public string Platform => "tiktok";

    public async Task<TokenRefreshResult?> RefreshTokenAsync(SocialAccount account, CancellationToken ct = default)
    {
        var refreshToken = account.RefreshTokenEncrypted;
        if (string.IsNullOrEmpty(refreshToken))
        {
            logger.LogWarning("TikTok account {AccountId} has no refresh token", account.PlatformAccountId);
            return null;
        }

        try
        {
            using var client = httpClientFactory.CreateClient("social-tiktok");
            var response = await client.PostAsync("https://open.tiktokapis.com/v2/oauth/token/", new FormUrlEncodedContent(
            [
                new("client_key", "TODO"), // Inject from config
                new("client_secret", "TODO"),
                new("grant_type", "refresh_token"),
                new("refresh_token", refreshToken),
            ]), ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("TikTok token refresh failed for account {AccountId}: {StatusCode}",
                    account.PlatformAccountId, response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadFromJsonAsync<TiktokTokenResponse>(ct);
            if (json?.Data is null)
                return null;

            return new TokenRefreshResult(
                AccessToken: json.Data.AccessToken,
                RefreshToken: json.Data.RefreshToken,
                AccessTokenExpiryUtc: DateTime.UtcNow.AddSeconds(json.Data.ExpiresIn),
                RefreshTokenExpiryUtc: DateTime.UtcNow.AddSeconds(json.Data.RefreshTokenExpiryIn));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "TikTok token refresh failed for account {AccountId}", account.PlatformAccountId);
            return null;
        }
    }

    private sealed record TiktokTokenResponse(TiktokTokenData? Data);
    private sealed record TiktokTokenData(string AccessToken, string RefreshToken, int ExpiresIn, int RefreshTokenExpiryIn);
}
