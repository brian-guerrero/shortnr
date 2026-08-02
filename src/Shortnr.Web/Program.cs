using System.Threading.Channels;
using System.Threading.RateLimiting;
using DnsClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;
using Shortnr.Data;
using Shortnr.Web.Extensions;
using Shortnr.Web.Models;
using Shortnr.Web.Services;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Protocol;
using OpenIddict.Validation.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddRazorPages();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")).UseOpenIddict());
builder.Services.AddSingleton(Channel.CreateUnbounded<ClickRecord>());
builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var logger = sp.GetRequiredService<ILogger<GeoIpService>>();
    var configuredPath = config["GeoIp:DatabasePath"];
    var path = !string.IsNullOrWhiteSpace(configuredPath)
        ? configuredPath
        : Path.Combine(sp.GetRequiredService<IWebHostEnvironment>().WebRootPath, "data", "GeoLite2-City.mmdb");
    return new GeoIpService(path, logger);
});
builder.Services.AddHostedService<GeoIpUpdateService>();
builder.Services.AddHostedService<ClickBatchProcessor>();
builder.Services.AddSingleton<QrService>();

// User provisioning queue — drained by UserProvisioningProcessor on every login.
// Registered unconditionally so DI is always consistent; the processor is a no-op
// when auth is disabled.
builder.Services.AddSingleton(Channel.CreateUnbounded<PendingUserLogin>());
builder.Services.AddSingleton(Channel.CreateUnbounded<object>());
builder.Services.AddHostedService<UserProvisioningProcessor>();

// AI/MCP activity queue — drained by AiActivityProcessor. Registered unconditionally
// so DI is always consistent; nothing writes to it until an MCP tool is called.
builder.Services.AddSingleton(Channel.CreateUnbounded<AiActivityRecord>());
builder.Services.AddHostedService<AiActivityProcessor>();

builder.Services.AddScoped<UserIdentityService>();
builder.Services.AddHttpClient<DomainVerifierService>(client => client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.Configure<RateLimitingOptions>(builder.Configuration.GetSection("RateLimiting"));

// Enforces the per-IP shorten-form limit inside IndexModel.OnPost. Razor Pages do not honor
// [EnableRateLimiting] on handler methods (see dotnet/AspNetCore.Docs#28714) and a class-level
// attribute would also throttle the landing-page GET, so the shorten limit is applied manually.
builder.Services.AddSingleton<ShortenRateLimiter>();
builder.Services.AddSingleton<IDnsQuery>(new LookupClient());
builder.Services.AddSingleton<ITxtDnsResolver, DnsClientTxtResolver>();

builder.Services.AddOidcAuthentication(builder.Configuration, builder.Environment);

// OAuth 2.1 authorization server for MCP clients (OpenIddict), fronting the
// OIDC/Dex login above. No-ops when auth is disabled.
builder.Services.AddOAuthServer(builder.Configuration, builder.Environment);

// API-key authentication for /api/v1. Registered unconditionally so the
// policy resolves even when OIDC is disabled; with no keys in the database
// the endpoints simply always return 401 in that mode.
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
        // McpAuthenticationOptions forwards authentication to a "Bearer" scheme
        // by default; we have none (OpenIddict validation plays that role), so
        // the Mcp scheme only handles challenges (401 + WWW-Authenticate +
        // protected-resource metadata) while ApiKey/OpenIddict do the auth.
        options.ForwardAuthenticate = null;
        options.ResourceMetadata = new ProtectedResourceMetadata
        {
            Resource = oauthResource,
            AuthorizationServers = { oauthIssuer },
            ScopesSupported = [ApiKeyScopes.McpRead, ApiKeyScopes.McpWrite]
        };
    });

// Model Context Protocol server. The HTTP transport is stateless (each request
// is an independent JSON-RPC invocation) and tools are discovered from this
// assembly via the [McpServerTool] / [McpServerToolType] attributes.
builder.Services.AddMcpServer(options =>
{
    options.ServerInstructions = "shortnr MCP server: manage short links and link-in-bio pages. Read tools require the mcp:read scope, write tools require mcp:write.";
    options.ServerInfo = new Implementation
    {
        Name = "shortnr",
        Version = "1.0.0",
        Description = "URL shortener and link-in-bio management"
    };
})
.WithHttpTransport(options => options.Stateless = true)
.WithToolsFromAssembly()
.WithResourcesFromAssembly();

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
    // read vs write granularity from the principal's scope claims. When auth is
    // enabled the endpoint also accepts OAuth bearer tokens (OpenIddict
    // validation) and, on failure, challenges the MCP scheme so clients get the
    // RFC 9728 401 + WWW-Authenticate + protected-resource metadata.
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
