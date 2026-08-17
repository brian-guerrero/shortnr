using System.Net;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Tests.Integration.Infrastructure;

namespace Shortnr.Tests.Integration.Preview;

public class PreviewThemeTests : IAsyncLifetime
{
    private readonly ShortnrWebAppFactory _factory = new(authEnabled: true);

    public Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.ClickEvents.RemoveRange(db.ClickEvents);
        db.ShortenedUrls.RemoveRange(db.ShortenedUrls);
        db.Workspaces.RemoveRange(db.Workspaces);
        db.Users.RemoveRange(db.Users);
        db.SaveChanges();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    private async Task<long> SeedUserAndAuthenticateAsync(string subject = "user-1")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new User
        {
            Subject = subject,
            Issuer = ShortnrWebAppFactory.TestIssuer,
            Email = "test@example.com",
            CreatedAtUtc = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        _factory.Services.GetRequiredService<TestAuthState>()
            .SetAuthenticatedUser(subject, ShortnrWebAppFactory.TestIssuer, "test@example.com");

        return user.Id;
    }

    [Fact]
    public async Task LinkPreviewTheme_PersistsOnCreate()
    {
        await SeedUserAndAuthenticateAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ShortenedUrls.Add(new ShortenedUrl
            {
                LongUrl = "https://example.com/target",
                ShortCode = "themed1",
                PreviewTheme = "default",
                CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var link = await db.ShortenedUrls.FirstOrDefaultAsync(l => l.ShortCode == "themed1");
            Assert.NotNull(link);
            Assert.Equal("default", link.PreviewTheme);
        }
    }

    [Fact]
    public async Task WorkspaceDefaultPreviewTheme_Persists()
    {
        var userId = await SeedUserAndAuthenticateAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var ws = new Workspace
            {
                Name = "Test Workspace",
                Slug = "test-ws",
                OwnerUserId = userId,
                DefaultPreviewTheme = "corporate",
                CreatedAtUtc = DateTime.UtcNow
            };
            db.Workspaces.Add(ws);
            await db.SaveChangesAsync();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var ws = await db.Workspaces.FirstOrDefaultAsync(w => w.Slug == "test-ws");
            Assert.NotNull(ws);
            Assert.Equal("corporate", ws.DefaultPreviewTheme);
        }
    }

    [Fact]
    public async Task Redirect_WithLinkTheme_RedirectsToPreviewPage()
    {
        await SeedUserAndAuthenticateAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ShortenedUrls.Add(new ShortenedUrl
            {
                LongUrl = "https://example.com/destination",
                ShortCode = "preview1",
                PreviewTheme = "default",
                CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClientNoRedirect();
        var response = await client.GetAsync("/preview1");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location?.ToString() ?? "";
        Assert.Contains("/preview?", location);
        Assert.Contains("theme=default", location);
        Assert.Contains("url=https", location);
    }

    [Fact]
    public async Task Redirect_WithWorkspaceTheme_RedirectsToPreviewPage()
    {
        var userId = await SeedUserAndAuthenticateAsync();

        long workspaceId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var ws = new Workspace
            {
                Name = "Theme WS",
                Slug = "theme-ws",
                OwnerUserId = userId,
                DefaultPreviewTheme = "dark",
                CreatedAtUtc = DateTime.UtcNow
            };
            db.Workspaces.Add(ws);
            await db.SaveChangesAsync();
            workspaceId = ws.Id;

            db.ShortenedUrls.Add(new ShortenedUrl
            {
                LongUrl = "https://example.com/ws-dest",
                ShortCode = "wsprev",
                WorkspaceId = workspaceId,
                CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClientNoRedirect();
        var response = await client.GetAsync("/wsprev");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location?.ToString() ?? "";
        Assert.Contains("theme=dark", location);
    }

    [Fact]
    public async Task Redirect_LinkThemeOverridesWorkspaceTheme()
    {
        var userId = await SeedUserAndAuthenticateAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var ws = new Workspace
            {
                Name = "Override WS",
                Slug = "override-ws",
                OwnerUserId = userId,
                DefaultPreviewTheme = "corporate",
                CreatedAtUtc = DateTime.UtcNow
            };
            db.Workspaces.Add(ws);
            await db.SaveChangesAsync();

            db.ShortenedUrls.Add(new ShortenedUrl
            {
                LongUrl = "https://example.com/override",
                ShortCode = "override",
                WorkspaceId = ws.Id,
                PreviewTheme = "minimal",
                CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClientNoRedirect();
        var response = await client.GetAsync("/override");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location?.ToString() ?? "";
        Assert.Contains("theme=minimal", location);
        Assert.DoesNotContain("theme=corporate", location);
    }

    [Fact]
    public async Task Redirect_NoTheme_DoesDirectRedirect()
    {
        await SeedUserAndAuthenticateAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ShortenedUrls.Add(new ShortenedUrl
            {
                LongUrl = "https://example.com/notheme",
                ShortCode = "notheme",
                CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClientNoRedirect();
        var response = await client.GetAsync("/notheme");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("https://example.com/notheme", response.Headers.Location?.ToString());
    }

    [Theory]
    [InlineData("default")]
    [InlineData("sunset")]
    [InlineData("ocean")]
    [InlineData("forest")]
    [InlineData("midnight")]
    [InlineData("minimal")]
    [InlineData("corporate")]
    [InlineData("dark")]
    public async Task ThemeCssFiles_LoadWithoutErrors(string theme)
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync($"/css/themes/preview-{theme}.css");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("--pv-bg", content);
        Assert.Contains("--pv-fg", content);
    }

    [Fact]
    public async Task PreviewPage_RendersWithTheme()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/preview?url=https%3A%2F%2Fexample.com%2Fdest&theme=default&host=example.com");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("data-preview-theme=\"default\"", content);
        Assert.Contains("preview-default.css", content);
        Assert.Contains("example.com", content);
    }

    [Theory]
    [InlineData("default")]
    [InlineData("sunset")]
    [InlineData("ocean")]
    [InlineData("forest")]
    [InlineData("midnight")]
    [InlineData("minimal")]
    [InlineData("corporate")]
    [InlineData("dark")]
    public async Task PreviewPage_AppliesBrutalistStylingRegardlessOfTheme(string theme)
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync($"/preview?url=https%3A%2F%2Fexample.com%2Fdest&theme={theme}&host=example.com");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains($"data-preview-theme=\"{theme}\"", content);
        Assert.Contains("border-radius: 0", content);
        Assert.Contains("var(--sn-shadow-offset-lg)", content);
        Assert.Contains("Powered by", content);
        Assert.Contains("class=\"brand-footer\"", content);
    }

    [Fact]
    public async Task PreviewPage_InvalidTheme_FallsBackToDefault()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/preview?url=https%3A%2F%2Fexample.com%2Fdest&theme=invalid&host=example.com");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("data-preview-theme=\"default\"", content);
    }
}
