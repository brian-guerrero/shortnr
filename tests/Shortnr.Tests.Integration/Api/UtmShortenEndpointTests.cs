using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Tests.Integration.Infrastructure;

namespace Shortnr.Tests.Integration.Api;

/// <summary>
/// Verifies that submitting UTM fields on the index page appends them to the
/// stored destination URL and persists the components separately on the
/// 1:1 metadata row, while blank UTM fields keep the link metadata-free.
/// </summary>
public class UtmShortenEndpointTests : IAsyncLifetime
{
    private static readonly Regex AntiforgeryTokenRegex = new(
        @"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""",
        RegexOptions.Compiled);

    private readonly ShortnrWebAppFactory _factory = new(authEnabled: false);

    public Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.ClickEvents.RemoveRange(db.ClickEvents);
        db.ShortenedUrls.RemoveRange(db.ShortenedUrls);
        db.Domains.RemoveRange(db.Domains);
        db.Users.RemoveRange(db.Users);
        return db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    [Fact]
    public async Task PostWithUtmFields_StoresAppendedUrlAndSeparateComponents()
    {
        var client = _factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, token,
            ("url", "https://example.com/landing"),
            ("utm_source", "newsletter"),
            ("utm_medium", "email"),
            ("utm_campaign", "spring-sale-2026"),
            ("utm_term", "running-shoes"),
            ("utm_content", "hero-banner"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var link = await db.ShortenedUrls
            .Include(l => l.Metadata)
            .SingleAsync(l => l.LongUrl.StartsWith("https://example.com/landing"));

        Assert.Equal(
            "https://example.com/landing?utm_source=newsletter&utm_medium=email&utm_campaign=spring-sale-2026&utm_term=running-shoes&utm_content=hero-banner",
            link.LongUrl);
        Assert.NotNull(link.Metadata);
        Assert.Equal("newsletter", link.Metadata.UtmSource);
        Assert.Equal("email", link.Metadata.UtmMedium);
        Assert.Equal("spring-sale-2026", link.Metadata.UtmCampaign);
        Assert.Equal("running-shoes", link.Metadata.UtmTerm);
        Assert.Equal("hero-banner", link.Metadata.UtmContent);
    }

    [Fact]
    public async Task PostWithUtmFields_UrlAlreadyHasQuery_AppendsParams()
    {
        var client = _factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, token,
            ("url", "https://example.com/landing?ref=xyz"),
            ("utm_source", "facebook"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var link = await db.ShortenedUrls
            .Include(l => l.Metadata)
            .SingleAsync(l => l.LongUrl.StartsWith("https://example.com/landing"));

        Assert.Equal("https://example.com/landing?ref=xyz&utm_source=facebook", link.LongUrl);
        Assert.Equal("facebook", link.Metadata!.UtmSource);
    }

    [Fact]
    public async Task PostSameUrl_DifferentUtm_CreatesSeparateLinks()
    {
        var client = _factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        await PostFormAsync(client, token,
            ("url", "https://example.com/landing"),
            ("utm_campaign", "campaign-a"));
        await PostFormAsync(client, token,
            ("url", "https://example.com/landing"),
            ("utm_campaign", "campaign-b"));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var links = await db.ShortenedUrls.Where(l => l.LongUrl.Contains("utm_campaign=")).ToListAsync();
        Assert.Equal(2, links.Count);
    }

    [Fact]
    public async Task PostWithoutUtmFields_CreatesNoMetadataRow()
    {
        var client = _factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, token,
            ("url", "https://example.com/plain"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var link = await db.ShortenedUrls.Include(l => l.Metadata).SingleAsync(l => l.LongUrl == "https://example.com/plain");
        Assert.Null(link.Metadata);
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        var page = await client.GetAsync("/");
        var html = await page.Content.ReadAsStringAsync();
        var match = AntiforgeryTokenRegex.Match(html);
        Assert.True(match.Success, "Antiforgery token not found in index page.");
        return match.Groups[1].Value;
    }

    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string token, params (string Name, string Value)[] fields)
    {
        var pairs = fields
            .Select(f => new KeyValuePair<string, string>(f.Name, f.Value))
            .ToList();
        pairs.Add(new KeyValuePair<string, string>("__RequestVerificationToken", token));
        return await client.PostAsync("/", new FormUrlEncodedContent(pairs));
    }
}
