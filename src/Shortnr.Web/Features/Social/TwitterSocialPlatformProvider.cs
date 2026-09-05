using Shortnr.Data.Entities;

namespace Shortnr.Web.Features.Social;

/// <summary>
/// Twitter (X) OAuth 2.0 token refresh. Uses PKCE flow — refresh tokens are
/// obtained during the initial authorization and exchanged here.
/// </summary>
public sealed class TwitterSocialPlatformProvider(
    IHttpClientFactory httpClientFactory,
    ILogger<TwitterSocialPlatformProvider> logger) : ISocialPlatformProvider
{
    public string Platform => "twitter";

    public async Task<TokenRefreshResult?> RefreshTokenAsync(SocialAccount account, CancellationToken ct = default)
    {
        var refreshToken = account.RefreshTokenEncrypted;
        if (string.IsNullOrEmpty(refreshToken))
        {
            logger.LogWarning("Twitter account {AccountId} has no refresh token", account.PlatformAccountId);
            return null;
        }

        try
        {
            using var client = httpClientFactory.CreateClient("social-twitter");
            var response = await client.PostAsync("https://api.twitter.com/2/oauth2/token", new FormUrlEncodedContent(
            [
                new("grant_type", "refresh_token"),
                new("refresh_token", refreshToken),
                new("client_id", "TODO"), // Inject from config
            ]), ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Twitter token refresh failed for account {AccountId}: {StatusCode}",
                    account.PlatformAccountId, response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadFromJsonAsync<TwitterTokenResponse>(ct);
            if (json is null)
                return null;

            return new TokenRefreshResult(
                AccessToken: json.AccessToken,
                RefreshToken: json.RefreshToken,
                AccessTokenExpiryUtc: DateTime.UtcNow.AddSeconds(json.ExpiresIn),
                RefreshTokenExpiryUtc: json.RefreshTokenExpiresIn > 0
                    ? DateTime.UtcNow.AddSeconds(json.RefreshTokenExpiresIn)
                    : null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Twitter token refresh failed for account {AccountId}", account.PlatformAccountId);
            return null;
        }
    }

    private sealed record TwitterTokenResponse(
        string AccessToken,
        string? RefreshToken,
        int ExpiresIn,
        int RefreshTokenExpiresIn);
}
