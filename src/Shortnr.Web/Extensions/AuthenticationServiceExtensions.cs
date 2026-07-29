using System.Security.Claims;
using System.Threading.Channels;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Shortnr.Web.Models;

namespace Shortnr.Web.Extensions;

public static class AuthenticationServiceExtensions
{
    /// <summary>
    /// Registers cookie + OIDC authentication when <c>Authentication:Enabled</c> is true.
    /// No-ops silently when disabled so the rest of the pipeline is unaffected.
    /// </summary>
    public static IServiceCollection AddOidcAuthentication(this IServiceCollection services, IConfiguration config, IWebHostEnvironment env)
    {
        if (!config.GetValue<bool>("Authentication:Enabled", defaultValue: true))
            return services;

        services.AddAuthentication(options =>
        {
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            options.DefaultSignOutScheme = OpenIdConnectDefaults.AuthenticationScheme;
        })
        .AddCookie()
        .AddOpenIdConnect(options =>
        {
            options.Authority = config["Authentication:Oidc:Authority"];
            options.ClientId = config["Authentication:Oidc:ClientId"];
            options.ClientSecret = config["Authentication:Oidc:ClientSecret"];
            options.CallbackPath = config["Authentication:Oidc:CallbackPath"] ?? "/signin-oidc";
            options.ResponseType = "code";
            options.SaveTokens = true;
            options.Scope.Add("profile");
            options.Scope.Add("email");
            // Dex runs over plain HTTP in local/test environments (no TLS termination).
            options.RequireHttpsMetadata = !env.IsDevelopment();

            options.Events = new OpenIdConnectEvents
            {
                OnTokenValidated = context =>
                {
                    var principal = context.Principal;
                    var subject = principal?.FindFirstValue(ClaimTypes.NameIdentifier)
                               ?? principal?.FindFirstValue("sub");

                    if (subject is not null)
                    {
                        var queue = context.HttpContext.RequestServices
                            .GetRequiredService<Channel<PendingUserLogin>>();

                        queue.Writer.TryWrite(new PendingUserLogin
                        {
                            Issuer = context.Options.Authority ?? string.Empty,
                            Subject = subject,
                            Email = principal!.FindFirstValue(ClaimTypes.Email) ?? principal.FindFirstValue("email"),
                            Name = principal.FindFirstValue(ClaimTypes.Name) ?? principal.FindFirstValue("name")
                        });
                    }

                    return Task.CompletedTask;
                }
            };
        });

        return services;
    }
}
