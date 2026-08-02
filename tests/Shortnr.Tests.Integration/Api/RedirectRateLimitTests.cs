using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Tests.Integration.Infrastructure;

namespace Shortnr.Tests.Integration.Api;

/// <summary>
/// Verifies the public redirect endpoint (GET /{shortCode}) is rate limited per
/// client IP, using the far more generous redirect limits.
/// </summary>
public class RedirectRateLimitTests : IAsyncLifetime
{
    private readonly LowRedirectLimitFactory _factory = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    [Fact]
    public async Task Redirect_AfterPerMinuteCap_ReturnsTooManyRequests()
    {
        const string code = "abc123";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ShortenedUrls.Add(new ShortenedUrl
            {
                LongUrl = "https://example.com/target",
                ShortCode = code,
                CreatedAtUtc = DateTime.UtcNow
            });
            db.SaveChanges();
        }

        var client = _factory.CreateClientNoRedirect();

        // LowRedirectLimitFactory sets Redirect:PerMinute=3. The first three GETs
        // redirect; the fourth should be rejected with 429.
        for (var i = 0; i < 3; i++)
        {
            var ok = await client.GetAsync($"/{code}");
            Assert.Equal(HttpStatusCode.Found, ok.StatusCode);
        }

        var rejected = await client.GetAsync($"/{code}");
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    }

    private sealed class LowRedirectLimitFactory : ShortnrWebAppFactory
    {
        public LowRedirectLimitFactory() : base(authEnabled: false)
        {
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["RateLimiting:Redirect:PerMinute"] = "3",
                    ["RateLimiting:Redirect:PerDay"] = "3"
                }));
        }
    }
}
