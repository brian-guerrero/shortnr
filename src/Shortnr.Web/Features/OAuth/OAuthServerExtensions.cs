using System.Security.Claims;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OpenIddict;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore;
using OpenIddict.Server;
using OpenIddict.Server.AspNetCore;
using OpenIddict.Validation;
using OpenIddict.Validation.AspNetCore;
using Shortnr.Data;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Shortnr.Web.Features.OAuth;

/// <summary>
/// OAuth 2.1 authorization server for MCP clients, powered by OpenIddict and
/// fronting the existing OIDC/Dex login as the identity provider. The AS lives
/// in this same process as the <c>/mcp</c> endpoint: RFC 7591 dynamic client
/// registration at <c>/connect/register</c>, authorize + token passthrough at
/// <c>/connect/*</c>, and the RFC 9728 protected-resource metadata is served by
/// the MCP authentication scheme registered in <c>Program.cs</c>.
/// </summary>
public static class OAuthServerExtensions
{
    /// <summary>Dev/test issuer; override via <c>OAuth:Issuer</c> config.</summary>
    public const string DefaultIssuer = "http://localhost:5156";

    /// <summary>The MCP resource URI, which is the OAuth audience (RFC 8707).</summary>
    public const string DefaultResource = "http://localhost:5156/mcp";

    public static string ResolveIssuer(IConfiguration config) =>
        config["OAuth:Issuer"] ?? DefaultIssuer;

    public static string ResolveResource(IConfiguration config) =>
        config["OAuth:Resource"] ?? DefaultResource;

    /// <summary>
    /// Registers CORS plus the OpenIddict server/validation services. Gated on
    /// <c>Authentication:Enabled</c>: without an IdP there is nothing to front,
    /// so the OAuth endpoints are simply not mapped and the MCP endpoint falls
    /// back to API-key auth.
    /// </summary>
    public static IServiceCollection AddOAuthServer(this IServiceCollection services, IConfiguration config, IWebHostEnvironment env)
    {
        // Browser MCP clients (e.g. the MCP Inspector UI) read the discovery
        // documents and the WWW-Authenticate challenge cross-origin.
        services.AddCors(options => options.AddDefaultPolicy(policy =>
            policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()
                  .WithExposedHeaders("WWW-Authenticate", "Mcp-Session-Id")));

        if (!config.GetValue<bool>("Authentication:Enabled", defaultValue: true))
            return services;

        var issuer = ResolveIssuer(config);
        var resource = ResolveResource(config);

        services.AddOpenIddict()
            .AddCore(options => options
                .UseEntityFrameworkCore()
                .UseDbContext<AppDbContext>())
            .AddServer(options =>
            {
                options.SetAuthorizationEndpointUris("/connect/authorize")
                       .SetTokenEndpointUris("/connect/token");

                // OAuth 2.1: authorization code + mandatory PKCE, plus refresh.
                options.AllowAuthorizationCodeFlow()
                       .RequireProofKeyForCodeExchange()
                       .AllowRefreshTokenFlow();

                // MCP scopes map 1:1 onto the API-key scopes so claim checks
                // stay uniform across both credential types.
                options.RegisterScopes(ApiKeyScopes.McpRead, ApiKeyScopes.McpWrite);

                // RFC 8707 resource binding: MCP clients send resource=&lt;mcp
                // url&gt; on authorize/token and OpenIddict rejects any value
                // not registered here (invalid_target).
                options.RegisterResources(resource);

                options.SetAccessTokenLifetime(TimeSpan.FromMinutes(
                    config.GetValue("OAuth:AccessTokenLifetimeMinutes", 60)));
                options.SetRefreshTokenLifetime(TimeSpan.FromDays(
                    config.GetValue("OAuth:RefreshTokenLifetimeDays", 14)));

                // Tokens stay readable with the published signing keys alone
                // (validation runs in-process, so encryption is not required).
                options.DisableAccessTokenEncryption();

                // Ephemeral dev-only certificates. Production must supply
                // signing/encryption certs via config (follow-up).
                if (env.IsDevelopment())
                {
                    options.AddDevelopmentEncryptionCertificate()
                           .AddDevelopmentSigningCertificate();
                }

                var aspNetCore = options.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough();

                // Localhost/test runs are plain HTTP.
                if (!env.IsProduction())
                    aspNetCore.DisableTransportSecurityRequirement();

                // Clients only try DCR if the AS metadata advertises it.
                options.AddEventHandler<OpenIddictServerEvents.ApplyConfigurationResponseContext>(
                    handler => handler.UseInlineHandler(context =>
                    {
                        context.Response["registration_endpoint"] = $"{issuer}/connect/register";
                        return default;
                    }));
            })
            .AddValidation(options =>
            {
                options.AddAudiences(resource);
                // Same process as the server: validate locally, no network round-trips.
                options.UseLocalServer();
                options.UseAspNetCore();
            });

        return services;
    }

    /// <summary>
    /// Upserts the <c>mcp:read</c>/<c>mcp:write</c> scopes with the MCP resource
    /// URI so OpenIddict derives the token audience from the granted scopes.
    /// </summary>
    public static async Task EnsureOAuthScopesAsync(IServiceProvider services, IConfiguration config)
    {
        if (!config.GetValue<bool>("Authentication:Enabled", defaultValue: true))
            return;

        var manager = services.GetRequiredService<IOpenIddictScopeManager>();
        var resource = ResolveResource(config);

        foreach (var name in new[] { ApiKeyScopes.McpRead, ApiKeyScopes.McpWrite, Scopes.OfflineAccess })
        {
            var descriptor = new OpenIddictScopeDescriptor
            {
                Name = name,
                DisplayName = name,
                Resources = { resource }
            };

            var existing = await manager.FindByNameAsync(name);
            if (existing is not null)
                await manager.UpdateAsync(existing, descriptor);
            else
                await manager.CreateAsync(descriptor);
        }
    }

    /// <summary>
    /// Maps the OAuth endpoints when auth is enabled. Authorize issues codes to
    /// the browser-authenticated user (auto-consent v1 — the Dex login is the
    /// consent gate); token exchanges codes and refresh tokens; register
    /// implements RFC 7591 dynamic client registration.
    /// </summary>
    public static IEndpointRouteBuilder MapOAuthEndpoints(this IEndpointRouteBuilder app, IConfiguration config)
    {
        if (!config.GetValue<bool>("Authentication:Enabled", defaultValue: true))
            return app;

        app.MapMethods("/connect/authorize", [HttpMethods.Get, HttpMethods.Post], async (
            HttpContext context,
            IOpenIddictScopeManager scopeManager) =>
        {
            var request = context.GetOpenIddictServerRequest()
                ?? throw new InvalidOperationException("The OpenIddict request cannot be retrieved.");

            var result = await context.AuthenticateAsync();
            if (result.Principal is null)
            {
                // Browser leg: send the user to Dex via the existing OIDC flow,
                // then return them to the original authorize URL.
                return Results.Challenge(new AuthenticationProperties
                {
                    RedirectUri = context.Request.PathBase + context.Request.Path + context.Request.QueryString
                }, [OpenIdConnectDefaults.AuthenticationScheme]);
            }

            // v1 auto-consent: the Dex login is the consent gate. A dedicated
            // consent screen is tracked as a follow-up.
            var identity = new ClaimsIdentity(
                authenticationType: TokenValidationParameters.DefaultAuthenticationType,
                nameType: Claims.Name,
                roleType: Claims.Role);

            var subject = result.Principal.FindFirstValue(ClaimTypes.NameIdentifier)
                       ?? result.Principal.FindFirstValue("sub");
            var name = result.Principal.FindFirstValue(ClaimTypes.Name)
                    ?? result.Principal.FindFirstValue("name");

            if (subject is not null)
                identity.SetClaim(Claims.Subject, subject);
            if (name is not null)
                identity.SetClaim(Claims.Name, name);

            foreach (var scope in request.GetScopes())
                identity.SetClaim(ApiKeyScopes.ScopeClaim, scope);

            identity.SetScopes(request.GetScopes());
            identity.SetResources(await scopeManager.ListResourcesAsync(identity.GetScopes()).ToListAsync());
            identity.SetDestinations(_ => [Destinations.AccessToken]);

            return Results.SignIn(new ClaimsPrincipal(identity), properties: null,
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        });

        app.MapPost("/connect/token", async (HttpContext context) =>
        {
            var request = context.GetOpenIddictServerRequest()
                ?? throw new InvalidOperationException("The OpenIddict request cannot be retrieved.");

            if (!request.IsAuthorizationCodeGrantType() && !request.IsRefreshTokenGrantType())
                return Results.BadRequest(new
                {
                    error = Errors.UnsupportedGrantType,
                    error_description = "Only the authorization code and refresh token grants are supported."
                });

            // The principal stored with the code/refresh token carries the
            // scopes, resources and custom claims granted at /connect/authorize.
            var result = await context.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            if (result.Principal is null)
                return Results.BadRequest(new { error = Errors.InvalidGrant });

            return Results.SignIn(result.Principal, properties: null,
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        });

        app.MapPost("/connect/register", async (
            ClientRegistrationRequest request,
            IOpenIddictApplicationManager applications,
            IConfiguration c) =>
        {
            var resource = ResolveResource(c);

            // MCP security rules: redirect URIs must be HTTPS or loopback —
            // reject anything else outright.
            var redirectUris = new List<Uri>();
            foreach (var value in request.RedirectUris ?? [])
            {
                if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttps && !uri.IsLoopback))
                {
                    return Results.BadRequest(new
                    {
                        error = "invalid_redirect_uri",
                        error_description = $"Redirect URI '{value}' must be HTTPS or loopback."
                    });
                }
                redirectUris.Add(uri);
            }

            if (request.GrantTypes is { Length: > 0 } grantTypes &&
                grantTypes.Except(["authorization_code", "refresh_token"]).Any())
            {
                return Results.BadRequest(new
                {
                    error = "invalid_client_metadata",
                    error_description = "Only the authorization_code and refresh_token grant types are supported."
                });
            }

            var clientId = Guid.NewGuid().ToString("N");
            var descriptor = new OpenIddictApplicationDescriptor
            {
                ClientId = clientId,
                ClientType = ClientTypes.Public, // PKCE, no secret
                DisplayName = request.ClientName,
                Permissions =
                {
                    Permissions.Endpoints.Authorization,
                    Permissions.Endpoints.Token,
                    Permissions.GrantTypes.AuthorizationCode,
                    Permissions.GrantTypes.RefreshToken,
                    Permissions.ResponseTypes.Code,
                    Permissions.Prefixes.Scope + ApiKeyScopes.McpRead,
                    Permissions.Prefixes.Scope + ApiKeyScopes.McpWrite,
                    // Refresh tokens require the offline_access scope, which the
                    // DCR descriptor grants to every registered client.
                    Permissions.Prefixes.Scope + Scopes.OfflineAccess,
                    // Per-client counterpart of RegisterResources: without these
                    // the authorize request fails with "not allowed to use the resource(s)".
                    Permissions.Prefixes.Resource + resource
                }
            };
            foreach (var uri in redirectUris)
                descriptor.RedirectUris.Add(uri);

            await applications.CreateAsync(descriptor);

            // RFC 7591 §3.2.1: the response echoes the registered metadata.
            return Results.Created($"/connect/register/{clientId}", new
            {
                client_id = clientId,
                client_id_issued_at = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                client_name = request.ClientName,
                redirect_uris = redirectUris.Select(u => u.ToString()),
                grant_types = new[] { "authorization_code", "refresh_token" },
                response_types = new[] { "code" },
                token_endpoint_auth_method = "none"
            });
        });

        return app;
    }

    /// <summary>RFC 7591 dynamic client registration request body.</summary>
    public sealed class ClientRegistrationRequest
    {
        [System.Text.Json.Serialization.JsonPropertyName("client_name")]
        public string? ClientName { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("redirect_uris")]
        public string[]? RedirectUris { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("grant_types")]
        public string[]? GrantTypes { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("response_types")]
        public string[]? ResponseTypes { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("token_endpoint_auth_method")]
        public string? TokenEndpointAuthMethod { get; set; }
    }
}
