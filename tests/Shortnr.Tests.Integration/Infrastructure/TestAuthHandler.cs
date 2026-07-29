using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Shortnr.Tests.Integration.Infrastructure;

/// <summary>
/// Minimal authentication handler that reads from <see cref="TestAuthState"/>
/// instead of validating real credentials. Registered as the default scheme in
/// <see cref="ShortnrWebAppFactory"/> so all requests use it automatically.
/// </summary>
public class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    TestAuthState authState)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (authState.Principal is null)
            return Task.FromResult(AuthenticateResult.NoResult());

        var ticket = new AuthenticationTicket(authState.Principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
