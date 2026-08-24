using Shortnr.Data.Entities;

namespace Shortnr.Web.Features.Social;

/// <summary>
/// Abstraction for fetching data from a social platform (PRD-021).
/// Each platform implements this interface so the bio-page layer can swap
/// providers without changes. Social platform APIs change frequently —
/// isolating implementations behind this boundary keeps the rest of the
/// codebase stable.
/// </summary>
public interface ISocialPlatformProvider
{
    /// <summary>The platform this provider handles.</summary>
    SocialProvider Provider { get; }

    /// <summary>
    /// Fetches the latest posts/clips and follower/subscriber count from the
    /// platform. Returns null on transient failure so callers can fall back
    /// to cached data.
    /// </summary>
    Task<SocialData?> FetchDataAsync(SocialAccount account, CancellationToken ct = default);

    /// <summary>
    /// Exchanges an authorization code for OAuth tokens. Returns the token
    /// tuple on success, null on failure.
    /// </summary>
    Task<OAuthTokens?> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct = default);

    /// <summary>
    /// Builds the OAuth authorization URL that the user should be redirected to.
    /// </summary>
    string BuildAuthorizationUrl(string redirectUri, string state);

    /// <summary>
    /// Returns the OAuth scopes required by this platform.
    /// </summary>
    IReadOnlyList<string> RequiredScopes { get; }
}
