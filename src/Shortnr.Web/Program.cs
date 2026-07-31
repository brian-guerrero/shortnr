using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
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

builder.Services.AddOidcAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddAuthorization();

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

app.MapDefaultEndpoints();
app.MapRazorPages();
app.MapAuthenticationEndpoints(app.Configuration);

app.MapApiEndpoints();

app.Run();

// Exposed for WebApplicationFactory<Program> in integration tests.
public partial class Program { }
