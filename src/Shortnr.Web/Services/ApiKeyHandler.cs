using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shortnr.Data;

namespace Shortnr.Web.Services;

/// <summary>
/// Authenticates <c>Authorization: Bearer &lt;key&gt;</c> against the ApiKeys table.
/// On success the principal carries the resolved <c>Users.Id</c> as
/// <see cref="ClaimTypes.NameIdentifier"/> plus a marker claim so
/// <see cref="UserIdentityService"/> treats it as a direct id rather than an
/// OIDC subject.
/// </summary>
public class ApiKeyHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    AppDbContext db) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";
    public const string ApiKeyIdClaim = "snr_api_key";
    /// <summary>Carries the real <c>ApiKeys.Id</c> (unlike <see cref="ApiKeyIdClaim"/> which
    /// carries the owner id as a marker for <see cref="UserIdentityService"/>).</summary>
    public const string ApiKeyIdValueClaim = "snr_api_key_id";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var key = header["Bearer ".Length..].Trim();
        if (key.Length == 0)
            return AuthenticateResult.Fail("Empty API key.");

        var hash = ApiKeyService.HashKey(key);
        var apiKey = await db.ApiKeys
            .FirstOrDefaultAsync(k => k.KeyHash == hash && k.RevokedAt == null);
        if (apiKey is null)
            return AuthenticateResult.Fail("Invalid API key.");

        apiKey.LastUsedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, apiKey.OwnerUserId.ToString()),
            new(ApiKeyIdClaim, apiKey.OwnerUserId.ToString()),
            new(ApiKeyIdValueClaim, apiKey.Id.ToString())
        };
        foreach (var scope in ApiKeyScopes.Resolve(apiKey.Scopes))
            claims.Add(new Claim(ApiKeyScopes.ScopeClaim, scope));

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }
}
