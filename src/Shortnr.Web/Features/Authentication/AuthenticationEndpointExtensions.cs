using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;

namespace Shortnr.Web.Features.Authentication;

public static class AuthenticationEndpointExtensions
{
    /// <summary>
    /// Maps <c>/account/login</c>, <c>/account/logout</c>, and <c>/workspace/switch</c>
    /// when <c>Authentication:Enabled</c> is true.
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

        app.MapPost("/workspace/switch", async ([FromForm] string slug, HttpContext ctx, WorkspaceService workspaceService, UserIdentityService identity) =>
        {
            var userId = await identity.ResolveOwnerUserIdAsync(ctx.User);
            if (userId is null)
                return Results.Unauthorized();

            if (string.IsNullOrWhiteSpace(slug) || slug == "personal")
            {
                ctx.Response.Cookies.Delete("snr_workspace");
                return Results.Redirect("/");
            }

            var isMember = await workspaceService.IsMemberAsync(
                (await workspaceService.GetWorkspaceBySlugAsync(slug))?.Id ?? 0,
                userId.Value);
            if (!isMember)
                return Results.Redirect("/");

            ctx.Response.Cookies.Append("snr_workspace", slug, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                MaxAge = TimeSpan.FromDays(30)
            });
            return Results.Redirect("/");
        }).DisableAntiforgery();

        return app;
    }
}
