using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Tests.Integration.Infrastructure;

namespace Shortnr.Tests.Integration.Api;

/// <summary>
/// Verifies custom (vanity) slug creation, collision and validation handling on
/// the index page, plus that the default-domain filtered unique index rejects
/// duplicate slugs at the database level.
/// </summary>
public class ShortenEndpointTests : IntegrationTestBase
{
    protected override bool AuthEnabled => false;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.ShortenedUrls.Add(new ShortenedUrl
        {
            LongUrl = "https://example.com/duplicate",
            ShortCode = "dup123",
            CreatedAtUtc = DateTime.UtcNow
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task PostWithCustomSlug_CreatesLinkWithThatCode()
    {
        var client = Factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, token,
            ("url", "https://example.com/vanity"),
            ("slug", "my-link"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("my-link", body);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var link = await db.ShortenedUrls.SingleAsync(l => l.ShortCode == "my-link");
        Assert.Equal("https://example.com/vanity", link.LongUrl);
        Assert.Null(link.DomainId);
    }

    [Fact]
    public async Task PostWithCollidingSlug_ReturnsErrorAndCreatesNothing()
    {
        var client = Factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, token,
            ("url", "https://example.com/other"),
            ("slug", "dup123"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("already taken", body);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.ShortenedUrls.CountAsync(l => l.ShortCode == "dup123"));
    }

    [Fact]
    public async Task PostWithInvalidSlug_ReturnsValidationError()
    {
        var client = Factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, token,
            ("url", "https://example.com/other"),
            ("slug", "not valid!"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Custom code", body);
    }

    [Fact]
    public async Task PostWithoutSlug_GeneratesSixCharacterCode()
    {
        var client = Factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, token,
            ("url", "https://example.com/random"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var link = await db.ShortenedUrls.SingleAsync(l => l.LongUrl == "https://example.com/random");
        Assert.Equal(6, link.ShortCode.Length);
    }

    [Fact]
    public async Task PostSameUrlWithoutSlug_ReturnsExistingCode()
    {
        var client = Factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, token,
            ("url", "https://example.com/duplicate"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("dup123", body);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.ShortenedUrls.CountAsync(l => l.ShortCode == "dup123"));
    }

    [Fact]
    public async Task DefaultDomainDuplicateSlug_IsRejectedByDatabaseIndex()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.ShortenedUrls.AddAsync(new ShortenedUrl
        {
            LongUrl = "https://example.com/one",
            ShortCode = "same-code",
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<DbUpdateException>(() =>
        {
            db.ShortenedUrls.Add(new ShortenedUrl
            {
                LongUrl = "https://example.com/two",
                ShortCode = "same-code",
                CreatedAtUtc = DateTime.UtcNow
            });
            return db.SaveChangesAsync();
        });
    }

    private static Task<string> GetAntiforgeryTokenAsync(HttpClient client) =>
        TestForms.GetAntiforgeryTokenAsync(client, "/");

    private static Task<HttpResponseMessage> PostFormAsync(HttpClient client, string token, params (string Name, string Value)[] fields) =>
        TestForms.PostFormAsync(client, "/", token, fields);
}
