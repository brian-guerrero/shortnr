using Shortnr.Data.Entities;

namespace Shortnr.Web.Features.Social;

/// <summary>
/// Instagram (Facebook Graph API) token refresh. Uses the long-lived token exchange endpoint.
/// </summary>
public sealed class InstagramSocialPlatformProvider(
    IHttpClientFactory httpClientFactory,
    ILogger<InstagramSocialPlatformProvider> logger) : ISocialPlatformProvider
{
    public string Platform => "instagram";

    public async Task<TokenRefreshResult?> RefreshTokenAsync(SocialAccount account, CancellationToken ct = default)
    {
        var refreshToken = account.RefreshTokenEncrypted;
        if (string.IsNullOrEmpty(refreshToken))
        {
            logger.LogWarning("Instagram account {AccountId} has no refresh token", account.PlatformAccountId);
            return null;
        }

        try
        {
            using var client = httpClientFactory.CreateClient("social-instagram");
            var response = await client.GetAsync(
                $"https://graph.facebook.com/v19.0/oauth/access_token" +
                $"?grant_type=fb_exchange_token" +
                $"&client_id=TODO" + // Inject from config
                $"&client_secret=TODO" +
                $"&fb_exchange_token={Uri.EscapeDataString(refreshToken)}", ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Instagram token refresh failed for account {AccountId}: {StatusCode}",
                    account.PlatformAccountId, response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadFromJsonAsync<InstagramTokenResponse>(ct);
            if (json is null)
                return null;

            return new TokenRefreshResult(
                AccessToken: json.AccessToken,
                RefreshToken: json.AccessToken, // Instagram long-lived tokens are the "refresh" token
                AccessTokenExpiryUtc: DateTime.UtcNow.AddDays(60), // Long-lived tokens last ~60 days
                RefreshTokenExpiryUtc: DateTime.UtcNow.AddDays(60));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Instagram token refresh failed for account {AccountId}", account.PlatformAccountId);
            return null;
        }
    }

    private sealed record InstagramTokenResponse(string AccessToken);
}
