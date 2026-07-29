using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

namespace Shortnr.Web.Extensions;

public static class AuthenticationEndpointExtensions
{
    /// <summary>
    /// Maps <c>/account/login</c> and <c>/account/logout</c> when <c>Authentication:Enabled</c> is true.
    /// </summary>
    public static IEndpointRouteBuilder MapAuthenticationEndpoints(this IEndpointRouteBuilder app, IConfiguration config)
    {
        if (!config.GetValue<bool>("Authentication:Enabled", defaultValue: true))
            return app;

        app.MapGet("/account/login", (string? returnUrl) =>
        {
            var redirectUri = returnUrl is not null && Uri.IsWellFormedUriString(returnUrl, UriKind.Relative)
                ? returnUrl
                : "/";
            return Results.Challenge(
                new AuthenticationProperties { RedirectUri = redirectUri },
                [OpenIdConnectDefaults.AuthenticationScheme]);
        });

        app.MapGet("/account/logout", () =>
            Results.SignOut(
                new AuthenticationProperties { RedirectUri = "/" },
                [CookieAuthenticationDefaults.AuthenticationScheme]));

        return app;
    }
}
