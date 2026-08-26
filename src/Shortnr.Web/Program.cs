using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;
using Shortnr.Data;
using Shortnr.Web.Features.Social;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Protocol;
using OpenIddict.Validation.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Validate scopes in development to catch DI lifetime mistakes before production
// (article: "Validate scopes before production does it for you"). This surfaces
// singletons capturing scoped services, missing registrations, and other
// lifetime bugs that would otherwise only appear under load.
builder.Host.UseDefaultServiceProvider((context, options) =>
{
    if (context.HostingEnvironment.IsDevelopment())
    {
        options.ValidateScopes = true;
        options.ValidateOnBuild = true;
    }
});

builder.AddServiceDefaults();

// Persist the Data Protection key ring to the mounted volume (same one the
// SQLite DB lives on — see Dockerfile) rather than the container's ephemeral
// filesystem. Without this, keys regenerate on every redeploy/cold start,
// silently invalidating every outstanding auth cookie and OIDC correlation
// cookie — that's what "keeps logging users out" without any visible error.
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("Shortnr");
var dataProtectionKeyPath = builder.Configuration["DataProtection:KeyPath"];
if (!string.IsNullOrEmpty(dataProtectionKeyPath))
{
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeyPath));
}

builder.Services.AddRazorPages();
// Resolve the provider/connection string lazily, inside the options callback,
// rather than eagerly against builder.Configuration here. AddDbContext's
// callback runs at DI-container build time, after all configuration sources
// (including test hosts' ConfigureAppConfiguration overrides, which
// WebApplicationFactory only merges in when builder.Build() runs) have been
// merged. Resolving eagerly at this point in the top-level statements would
// capture appsettings.json's default connection string instead of a test
// factory's per-test override, silently pointing every test at the same
// on-disk shortnr.db file.
builder.Services.AddDbContext<AppDbContext>(options =>
{
    var dbProvider = Shortnr.Data.DatabaseProviderHelper.ResolveProvider(builder.Configuration);
    var dbConnectionString = Shortnr.Data.DatabaseProviderHelper.ResolveConnectionString(builder.Configuration, dbProvider)
        ?? throw new InvalidOperationException(
            $"No connection string configured for database provider '{dbProvider}'. " +
            $"Set 'Database:ConnectionString'.");
    options.UseProvider(dbProvider, dbConnectionString);
});

// ── Feature module registrations ──────────────────────────────────────────────
// Each feature owns its own DI composition boundary (article: "treat DI
// registration as composition — each module should own its own registration
// boundary"). Program.cs wires the modules together; the modules decide how
// their own services are composed.
builder.Services
    .AddShortLinksFeature()
    .AddClickTrackingFeature()
    .AddGeoIpFeature()
    .AddAuthenticationFeature(builder.Configuration, builder.Environment)
    .AddDomainsFeature()
    .AddWorkspacesFeature()
    .AddBioPagesFeature()
    .AddThemingFeature(builder.Configuration)
    .AddWebhooksFeature()
    .AddApiFeature()
    .AddMcpFeature()
    .AddOAuthFeature(builder.Configuration, builder.Environment)
    .AddEmailFeature(builder.Configuration)
    .AddAiActivityFeature()
    .AddAiInsightsFeature(builder.Configuration)
    .AddInfrastructureFeature(builder.Configuration)
    .AddSocialFeature(builder.Configuration);

// ── Cross-cutting: authentication schemes, policies, rate limiting ──────────
// These span multiple feature boundaries and are wired at the composition root.
var oauthResource = OAuthServerExtensions.ResolveResource(builder.Configuration);
var oauthIssuer = OAuthServerExtensions.ResolveIssuer(builder.Configuration);

builder.Services.AddAuthentication()
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, ApiKeyHandler>(
        ApiKeyHandler.SchemeName, _ => { })
    // The MCP scheme handles *challenges* (401 + WWW-Authenticate + serving the
    // RFC 9728 protected-resource-metadata document); OpenIddict validation and
    // ApiKeyHandler handle *authentication* of presented credentials.
    .AddMcp(options =>
    {
        options.ForwardAuthenticate = null;
        options.ResourceMetadata = new ProtectedResourceMetadata
        {
            Resource = oauthResource,
            AuthorizationServers = { oauthIssuer },
            ScopesSupported = [ApiKeyScopes.McpRead, ApiKeyScopes.McpWrite]
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(ApiKeyHandler.SchemeName, policy => policy
        .AddAuthenticationSchemes(ApiKeyHandler.SchemeName)
        .RequireAuthenticatedUser())
    .AddPolicy(ApiKeyScopes.LinksRead, policy => policy
        .AddAuthenticationSchemes(ApiKeyHandler.SchemeName)
        .RequireAuthenticatedUser()
        .RequireClaim(ApiKeyScopes.ScopeClaim, ApiKeyScopes.LinksRead))
    .AddPolicy(ApiKeyScopes.LinksWrite, policy => policy
        .AddAuthenticationSchemes(ApiKeyHandler.SchemeName)
        .RequireAuthenticatedUser()
        .RequireClaim(ApiKeyScopes.ScopeClaim, ApiKeyScopes.LinksWrite))
    .AddPolicy(ApiKeyScopes.McpRead, policy => policy
        .AddAuthenticationSchemes(ApiKeyHandler.SchemeName)
        .RequireAuthenticatedUser()
        .RequireClaim(ApiKeyScopes.ScopeClaim, ApiKeyScopes.McpRead))
    .AddPolicy(ApiKeyScopes.McpWrite, policy => policy
        .AddAuthenticationSchemes(ApiKeyHandler.SchemeName)
        .RequireAuthenticatedUser()
        .RequireClaim(ApiKeyScopes.ScopeClaim, ApiKeyScopes.McpWrite))
    // The /mcp endpoint itself only needs one mcp scope; individual tools enforce
    // read vs write granularity from the principal's scope claims.
    .AddPolicy("mcp", policy =>
    {
        policy.RequireAuthenticatedUser()
              .RequireAssertion(ctx =>
                  ctx.User.HasClaim(ApiKeyScopes.ScopeClaim, ApiKeyScopes.McpRead) ||
                  ctx.User.HasClaim(ApiKeyScopes.ScopeClaim, ApiKeyScopes.McpWrite));

        if (builder.Configuration.GetValue<bool>("Authentication:Enabled", defaultValue: true))
            policy.AddAuthenticationSchemes(
                ApiKeyHandler.SchemeName,
                OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme,
                McpAuthenticationDefaults.AuthenticationScheme);
        else
            policy.AddAuthenticationSchemes(ApiKeyHandler.SchemeName);
    });

// Per-key rate limiting: 60 requests/min burst + 1000/day cap. Partitioned by
// the presented (hashed) key so it works independently of auth ordering.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("api-key", context =>
        RateLimitPartition.Get(RateLimitPartitionKey(context), _ => new ChainedRateLimiter(
        [
            new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }),
            new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
            {
                PermitLimit = 1000,
                Window = TimeSpan.FromDays(1),
                QueueLimit = 0,
                AutoReplenishment = true
            })
        ])));

    // Per-key limiter for the MCP endpoint: slightly higher burst than the REST
    // API (an agent may enumerate tools + several reads in a minute) with the
    // same daily cap.
    options.AddPolicy("mcp-tools", context =>
        RateLimitPartition.Get(RateLimitPartitionKey(context), _ => new ChainedRateLimiter(
        [
            new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }),
            new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5000,
                Window = TimeSpan.FromDays(1),
                QueueLimit = 0,
                AutoReplenishment = true
            })
        ])));

    // IP-keyed limit on the public redirect endpoint. Deliberately far more generous
    // than the shorten one so legitimate traffic (including viral spikes) is never
    // throttled; operators expecting very high redirect volume should additionally
    // configure reverse-proxy/CDN limiting.
    options.AddPolicy("redirect-ip", context =>
    {
        var limits = context.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;
        return RateLimitPartition.Get(
            IpRateLimitPolicies.ResolveKey(context, limits.TrustForwardedFor),
            _ => IpRateLimitPolicies.Build(limits.Redirect.PerMinute, limits.Redirect.PerDay));
    });
});

builder.Services.AddOpenApi(options =>
{
    options.ShouldInclude = description =>
        description.RelativePath?.StartsWith("api/v1", StringComparison.OrdinalIgnoreCase) == true;
});

var app = builder.Build();

// Fly (and most PaaS reverse proxies) terminate TLS at the edge and forward
// plain HTTP internally, so without this the OIDC handler builds redirect_uri
// as http://... instead of https://..., which Auth0 rejects as an unregistered
// callback URL. Opt-in only (like RateLimiting:TrustForwardedFor) — blindly
// trusting X-Forwarded-* is only safe when a real proxy is guaranteed to
// overwrite client-supplied values before they reach the app; self-hosted
// instances without one in front must not enable this.
if (builder.Configuration.GetValue<bool>("Hosting:TrustForwardedHeaders", defaultValue: false))
{
    var forwardedHeadersOptions = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
    };
    // The proxy hop isn't a fixed/known address on Fly's network, so clear the
    // default network/proxy allowlist rather than reject its headers.
    forwardedHeadersOptions.KnownIPNetworks.Clear();
    forwardedHeadersOptions.KnownProxies.Clear();
    app.UseForwardedHeaders(forwardedHeadersOptions);
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
    await OAuthServerExtensions.EnsureOAuthScopesAsync(scope.ServiceProvider, app.Configuration);
}

app.UseStaticFiles();
app.UseRouting();
app.UseCors();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapDefaultEndpoints();
app.MapRazorPages();
app.MapAuthenticationEndpoints(app.Configuration);
app.MapCommunityThemeEndpoints();
app.MapThemeEndpoints(app.Configuration);

app.MapApiEndpoints();
app.MapApiV1Endpoints();
app.MapMcpEndpoints();
app.MapOAuthEndpoints(app.Configuration);

app.MapOpenApi();
app.MapScalarApiReference("/api/docs", options => options
    .WithTitle("shortnr API")
    .WithOpenApiRoutePattern("/openapi/{documentName}.json"));

// Groups rate-limit partitions by the hashed bearer key so every request from a
// key (REST or MCP) is throttled independently of auth ordering.
static string RateLimitPartitionKey(HttpContext context)
{
    var header = context.Request.Headers.Authorization.ToString();
    var key = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
        ? header["Bearer ".Length..].Trim()
        : "";
    return key.Length == 0 ? "anonymous" : ApiKeyService.HashKey(key);
}

app.Run();

// Exposed for WebApplicationFactory<Program> in integration tests.
public partial class Program { }
