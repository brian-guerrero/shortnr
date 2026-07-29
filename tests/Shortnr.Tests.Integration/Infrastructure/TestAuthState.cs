using System.Security.Claims;

namespace Shortnr.Tests.Integration.Infrastructure;

/// <summary>
/// Singleton service that integration tests use to control which principal
/// the <see cref="TestAuthHandler"/> presents to the request pipeline.
/// </summary>
public class TestAuthState
{
    public ClaimsPrincipal? Principal { get; private set; }

    public void SetAuthenticatedUser(string subject, string issuer, string? email = null, string? name = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, subject)
        };
        if (email is not null) claims.Add(new(ClaimTypes.Email, email));
        if (name is not null) claims.Add(new(ClaimTypes.Name, name));

        Principal = new ClaimsPrincipal(
            new ClaimsIdentity(claims, TestAuthHandler.SchemeName));
    }

    public void ClearUser() => Principal = null;
}
