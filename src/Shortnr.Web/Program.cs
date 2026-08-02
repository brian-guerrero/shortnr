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

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddRazorPages();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));
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

// API-key authentication for /api/v1. Registered unconditionally so the
// policy resolves even when OIDC is disabled; with no keys in the database
// the endpoints simply always return 401 in that mode.
builder.Services.AddAuthentication()
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, ApiKeyHandler>(
        ApiKeyHandler.SchemeName, _ => { });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(ApiKeyHandler.SchemeName, policy => policy
        .AddAuthenticationSchemes(ApiKeyHandler.SchemeName)
        .RequireAuthenticatedUser());

// Per-key rate limiting: 60 requests/min burst + 1000/day cap. Partitioned by
// the presented (hashed) key so it works independently of auth ordering.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("api-key", context =>
    {
        var header = context.Request.Headers.Authorization.ToString();
        var key = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..].Trim()
            : "";
        var partitionKey = key.Length == 0 ? "anonymous" : ApiKeyService.HashKey(key);

        return RateLimitPartition.Get(partitionKey, _ => new ChainedRateLimiter(
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
        ]));
    });

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
}

app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapDefaultEndpoints();
app.MapRazorPages();
app.MapAuthenticationEndpoints(app.Configuration);

app.MapApiEndpoints();
app.MapApiV1Endpoints();

app.MapOpenApi();
app.MapScalarApiReference("/api/docs", options => options
    .WithTitle("shortnr API")
    .WithOpenApiRoutePattern("/openapi/{documentName}.json"));

app.Run();

// Exposed for WebApplicationFactory<Program> in integration tests.
public partial class Program { }
