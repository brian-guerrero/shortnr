using System.Security.Claims;
using System.Threading.Channels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Web.Models;
using Shortnr.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddRazorPages();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddSingleton(Channel.CreateUnbounded<ClickRecord>());
builder.Services.AddHostedService<ClickBatchProcessor>();
builder.Services.AddSingleton<QrService>();

// User provisioning queue — only needed when auth is enabled, but registering it
// unconditionally keeps DI simple; the processor exits immediately when auth is off.
builder.Services.AddSingleton(Channel.CreateUnbounded<PendingUserLogin>());
builder.Services.AddHostedService<UserProvisioningProcessor>();

// Authentication is opt-in. Set Authentication:Enabled=false (or omit the OIDC
// config entirely) to run the app without any login requirement.
var authEnabled = builder.Configuration.GetValue<bool>("Authentication:Enabled", defaultValue: true);

if (authEnabled)
{
    builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
        options.DefaultSignOutScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
        .AddCookie()
        .AddOpenIdConnect(options =>
        {
            options.Authority = builder.Configuration["Authentication:Oidc:Authority"];
            options.ClientId = builder.Configuration["Authentication:Oidc:ClientId"];
            options.ClientSecret = builder.Configuration["Authentication:Oidc:ClientSecret"];
            options.CallbackPath = builder.Configuration["Authentication:Oidc:CallbackPath"] ?? "/signin-oidc";
            options.ResponseType = "code";
            options.SaveTokens = true;
            options.Scope.Add("profile");
            options.Scope.Add("email");
            // Dex runs over plain HTTP in local/test environments (no TLS termination).
            options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();

            options.Events = new OpenIdConnectEvents
            {
                OnTokenValidated = context =>
                {
                    var principal = context.Principal;
                    var subject = principal?.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal?.FindFirstValue("sub");
                    if (subject is not null)
                    {
                        var queue = context.HttpContext.RequestServices.GetRequiredService<Channel<PendingUserLogin>>();
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
}

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

if (authEnabled)
{
    app.MapGet("/account/login", (string? returnUrl) =>
    {
        var redirectUri = returnUrl is not null && Uri.IsWellFormedUriString(returnUrl, UriKind.Relative) ? returnUrl : "/";
        return Results.Challenge(new AuthenticationProperties { RedirectUri = redirectUri }, [OpenIdConnectDefaults.AuthenticationScheme]);
    });

    app.MapGet("/account/logout", () =>
        Results.SignOut(
            new AuthenticationProperties { RedirectUri = "/" },
            [CookieAuthenticationDefaults.AuthenticationScheme]));
}

app.MapGet("/api/qr/{shortCode}", (string shortCode, HttpContext ctx, QrService qr) =>
{
    var shortUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}/{shortCode}";
    var png = qr.GeneratePng(shortUrl);
    return Results.File(png, contentType: "image/png", fileDownloadName: $"qr-{shortCode}.png");
});

app.MapGet("/api/metrics", async (AppDbContext db) =>
{
    var totalLinks = await db.ShortenedUrls.CountAsync();
    var totalClicks = await db.ShortenedUrls.SumAsync(l => (long?)l.ClickCount) ?? 0;
    var topLinks = await db.ShortenedUrls
        .OrderByDescending(l => l.ClickCount)
        .Take(10)
        .Select(l => new { l.ShortCode, l.LongUrl, l.ClickCount })
        .ToListAsync();

    return Results.Json(new { totalLinks, totalClicks, topLinks });
});

app.MapGet("/{shortCode}", async (string shortCode, AppDbContext db, Channel<ClickRecord> clickChannel, HttpContext context) =>
{
    var link = await db.ShortenedUrls.FirstOrDefaultAsync(l => l.ShortCode == shortCode);
    if (link is null) return Results.NotFound();

    clickChannel.Writer.TryWrite(new ClickRecord
    {
        ShortCode = shortCode,
        IpAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        UserAgent = context.Request.Headers["User-Agent"].FirstOrDefault() ?? "",
        Referer = context.Request.Headers["Referer"].FirstOrDefault() ?? ""
    });

    return Results.Redirect(link.LongUrl);
});

app.Run();
