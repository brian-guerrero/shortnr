using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Tests.Integration.Infrastructure;

namespace Shortnr.Tests.Integration.Api;

/// <summary>
/// Verifies the /api/v1 campaign metadata surface: UTM params, retargeting pixel
/// selection, and platform deep links on create/update, the campaign filter on
/// list, and GET /api/v1/pixel-snippets. Mirrors the equivalent MCP tool coverage
/// (McpWriteToolsTests/McpReadToolsTests) since both surfaces share the same
/// underlying merge/validation semantics.
/// </summary>
public class ApiV1CampaignMetadataTests : IAsyncLifetime
{
    private const string TestKey = "snr_testkey1234567890abcdef1234567890";
    private readonly ShortnrWebAppFactory _factory = new(authEnabled: true);

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    private HttpClient AuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestKey);
        return client;
    }

    // -------------------------------------------------------------------------
    // POST /links — create with campaign metadata
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Create_WithUtmParams_AppendsToUrlAndReturnsMetadata()
    {
        await SeedUserAndKeyAsync("api-owner", TestKey);
        var client = AuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/v1/links", new CreateLinkRequest
        {
            Url = "https://example.com/spring",
            Metadata = new LinkMetadataRequest { UtmSource = "newsletter", UtmMedium = "email", UtmCampaign = "spring-sale" }
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LinkResponse>();
        Assert.NotNull(body);
        Assert.Contains("utm_source=newsletter", body.LongUrl);
        Assert.Contains("utm_medium=email", body.LongUrl);
        Assert.Contains("utm_campaign=spring-sale", body.LongUrl);
        Assert.NotNull(body.Metadata);
        Assert.Equal("newsletter", body.Metadata!.UtmSource);
        Assert.Equal("spring-sale", body.Metadata.UtmCampaign);
    }

    [Fact]
    public async Task Create_WithTemplatePixelSnippet_SavesMetadata()
    {
        await SeedUserAndKeyAsync("api-owner", TestKey);
        var client = AuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/v1/links", new CreateLinkRequest
        {
            Url = "https://example.com/x",
            Metadata = new LinkMetadataRequest { PixelSnippet = "Meta Pixel", PixelId = "1234567890" }
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LinkResponse>();
        Assert.NotNull(body?.Metadata);
        Assert.Equal("Meta Pixel", body!.Metadata!.PixelSnippet);
        Assert.Equal("1234567890", body.Metadata.PixelValue);
    }

    [Fact]
    public async Task Create_PixelSnippetWithoutValue_ReturnsValidationProblem()
    {
        await SeedUserAndKeyAsync("api-owner", TestKey);
        var client = AuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/v1/links", new CreateLinkRequest
        {
            Url = "https://example.com/x",
            Metadata = new LinkMetadataRequest { PixelSnippet = "Meta Pixel" }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadAsStringAsync();
        Assert.Contains("pixelId is required", problem);
    }

    [Fact]
    public async Task Create_UnknownPixelSnippet_ReturnsValidationProblem()
    {
        await SeedUserAndKeyAsync("api-owner", TestKey);
        var client = AuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/v1/links", new CreateLinkRequest
        {
            Url = "https://example.com/x",
            Metadata = new LinkMetadataRequest { PixelSnippet = "Not A Real Snippet" }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadAsStringAsync();
        Assert.Contains("No pixel snippet named", problem);
    }

    [Fact]
    public async Task Create_WithDeepLinks_SavesMetadata()
    {
        await SeedUserAndKeyAsync("api-owner", TestKey);
        var client = AuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/v1/links", new CreateLinkRequest
        {
            Url = "https://example.com/x",
            Metadata = new LinkMetadataRequest
            {
                IosDeepLink = "myapp://open",
                AndroidDeepLink = "https://play.google.com/store/apps/details?id=com.example.app"
            }
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LinkResponse>();
        Assert.NotNull(body?.Metadata);
        Assert.Equal("myapp://open", body!.Metadata!.IosDeepLink);
        Assert.Equal("https://play.google.com/store/apps/details?id=com.example.app", body.Metadata.AndroidDeepLink);
    }

    [Fact]
    public async Task Create_WithoutMetadata_ReturnsNullMetadata()
    {
        await SeedUserAndKeyAsync("api-owner", TestKey);
        var client = AuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/v1/links", new CreateLinkRequest { Url = "https://example.com/plain" });

        var body = await response.Content.ReadFromJsonAsync<LinkResponse>();
        Assert.NotNull(body);
        Assert.Null(body!.Metadata);
    }

    // -------------------------------------------------------------------------
    // PATCH /links/{shortCode} — merge semantics
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Patch_ChangesOneUtmField_KeepsOthers()
    {
        var owner = await SeedUserAndKeyAsync("api-owner", TestKey);
        var linkId = await SeedLinkAsync(owner, "camp001");
        await SeedMetadataAsync(linkId, utmCampaign: "old-campaign", utmSource: "newsletter");
        var client = AuthenticatedClient();

        var response = await client.PatchAsJsonAsync("/api/v1/links/camp001", new UpdateLinkRequest
        {
            Metadata = new LinkMetadataRequest { UtmCampaign = "new-campaign" }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LinkResponse>();
        Assert.NotNull(body?.Metadata);
        Assert.Equal("new-campaign", body!.Metadata!.UtmCampaign);
        Assert.Equal("newsletter", body.Metadata.UtmSource);
    }

    [Fact]
    public async Task Patch_ClearingUtmFieldWithEmptyString_RemovesJustThatField()
    {
        var owner = await SeedUserAndKeyAsync("api-owner", TestKey);
        var linkId = await SeedLinkAsync(owner, "camp002");
        await SeedMetadataAsync(linkId, utmCampaign: "spring-sale", utmSource: "newsletter");
        var client = AuthenticatedClient();

        var response = await client.PatchAsJsonAsync("/api/v1/links/camp002", new UpdateLinkRequest
        {
            Metadata = new LinkMetadataRequest { UtmSource = "" }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LinkResponse>();
        Assert.NotNull(body?.Metadata);
        Assert.Null(body!.Metadata!.UtmSource);
        Assert.Equal("spring-sale", body.Metadata.UtmCampaign);
    }

    [Fact]
    public async Task Patch_ClearingAllMetadataFields_RemovesMetadataRow()
    {
        var owner = await SeedUserAndKeyAsync("api-owner", TestKey);
        var linkId = await SeedLinkAsync(owner, "camp003");
        await SeedMetadataAsync(linkId, iosDeepLink: "myapp://open");
        var client = AuthenticatedClient();

        var response = await client.PatchAsJsonAsync("/api/v1/links/camp003", new UpdateLinkRequest
        {
            Metadata = new LinkMetadataRequest { IosDeepLink = "" }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LinkResponse>();
        Assert.Null(body?.Metadata);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.ShortenedUrlMetadatas.AnyAsync(m => m.ShortenedUrlId == linkId));
    }

    [Fact]
    public async Task Patch_PixelIdWithoutSelectedSnippet_ReturnsValidationProblem()
    {
        var owner = await SeedUserAndKeyAsync("api-owner", TestKey);
        await SeedLinkAsync(owner, "camp004");
        var client = AuthenticatedClient();

        var response = await client.PatchAsJsonAsync("/api/v1/links/camp004", new UpdateLinkRequest
        {
            Metadata = new LinkMetadataRequest { PixelId = "999" }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadAsStringAsync();
        Assert.Contains("No pixel snippet is currently attached", problem);
    }

    [Fact]
    public async Task Patch_WithoutMetadataField_LeavesExistingMetadataUntouched()
    {
        var owner = await SeedUserAndKeyAsync("api-owner", TestKey);
        var linkId = await SeedLinkAsync(owner, "camp005");
        await SeedMetadataAsync(linkId, utmCampaign: "spring-sale");
        var client = AuthenticatedClient();

        var response = await client.PatchAsJsonAsync("/api/v1/links/camp005", new UpdateLinkRequest { Title = "New title" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LinkResponse>();
        Assert.Equal("New title", body?.Title);
        Assert.NotNull(body?.Metadata);
        Assert.Equal("spring-sale", body!.Metadata!.UtmCampaign);
    }

    // -------------------------------------------------------------------------
    // GET /links — campaign filter, GET /pixel-snippets
    // -------------------------------------------------------------------------

    [Fact]
    public async Task List_FiltersByCampaign()
    {
        var owner = await SeedUserAndKeyAsync("api-owner", TestKey);
        var springLink = await SeedLinkAsync(owner, "spring1");
        await SeedMetadataAsync(springLink, utmCampaign: "spring-sale-2026");
        var summerLink = await SeedLinkAsync(owner, "summer1");
        await SeedMetadataAsync(summerLink, utmCampaign: "summer-sale-2026");
        await SeedLinkAsync(owner, "plain001");
        var client = AuthenticatedClient();

        var list = await client.GetFromJsonAsync<LinkListResponse>("/api/v1/links?campaign=spring");

        Assert.NotNull(list);
        Assert.Equal(1, list.Total);
        Assert.Equal("spring1", list.Links[0].ShortCode);
        Assert.Equal("spring-sale-2026", list.Links[0].Metadata?.UtmCampaign);
    }

    [Fact]
    public async Task ListPixelSnippets_ReturnsSeededSnippets()
    {
        await SeedUserAndKeyAsync("api-owner", TestKey);
        var client = AuthenticatedClient();

        var snippets = await client.GetFromJsonAsync<List<PixelSnippetResponse>>("/api/v1/pixel-snippets");

        Assert.NotNull(snippets);
        Assert.Contains(snippets!, s => s.Name == "Meta Pixel" && !s.IsCustom);
        Assert.Contains(snippets!, s => s.Name == "Custom snippet" && s.IsCustom);
    }

    [Fact]
    public async Task Get_IncludesCampaignMetadata()
    {
        var owner = await SeedUserAndKeyAsync("api-owner", TestKey);
        var linkId = await SeedLinkAsync(owner, "camp006");
        await SeedMetadataAsync(linkId, utmCampaign: "fall-sale", pixelSnippetId: 1, pixelId: "555");
        var client = AuthenticatedClient();

        var body = await client.GetFromJsonAsync<LinkResponse>("/api/v1/links/camp006");

        Assert.NotNull(body?.Metadata);
        Assert.Equal("fall-sale", body!.Metadata!.UtmCampaign);
        Assert.Equal("Meta Pixel", body.Metadata.PixelSnippet);
        Assert.Equal("555", body.Metadata.PixelValue);
    }

    // -------------------------------------------------------------------------
    // Seeding helpers
    // -------------------------------------------------------------------------

    private async Task<long> SeedUserAndKeyAsync(string subject, string key)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new User
        {
            Issuer = ShortnrWebAppFactory.TestIssuer,
            Subject = subject,
            CreatedAtUtc = DateTime.UtcNow,
            LastLoginAtUtc = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.ApiKeys.Add(new ApiKey
        {
            OwnerUserId = user.Id,
            KeyHash = ApiKeyService.HashKey(key),
            KeyPrefix = "snr_",
            Label = "test key",
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return user.Id;
    }

    private async Task<long> SeedLinkAsync(long ownerUserId, string shortCode)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var link = new ShortenedUrl
        {
            LongUrl = "https://example.com/seed",
            ShortCode = shortCode,
            OwnerUserId = ownerUserId,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.ShortenedUrls.Add(link);
        await db.SaveChangesAsync();
        return link.Id;
    }

    private async Task SeedMetadataAsync(long linkId, string? utmCampaign = null, string? utmSource = null,
        long? pixelSnippetId = null, string? pixelId = null, string? iosDeepLink = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.ShortenedUrlMetadatas.Add(new ShortenedUrlMetadata
        {
            ShortenedUrlId = linkId,
            UtmCampaign = utmCampaign,
            UtmSource = utmSource,
            PixelSnippetId = pixelSnippetId,
            PixelId = pixelId,
            IosDeepLink = iosDeepLink
        });
        await db.SaveChangesAsync();
    }
}
