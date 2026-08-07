using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Tests.Integration.Infrastructure;

namespace Shortnr.Tests.Integration.Api;

/// <summary>
/// Verifies the /api/v1 REST surface: API-key auth, create/list/get/update/delete
/// link CRUD, click pagination, owner scoping and the per-key rate limit.
/// </summary>
public class ApiV1EndpointsTests : IAsyncLifetime
{
    private const string TestKey = "snr_testkey1234567890abcdef1234567890";
    private readonly ShortnrWebAppFactory _factory = new(authEnabled: true);

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    [Fact]
    public async Task WithoutKey_ReturnsUnauthorized()
    {
        var response = await _factory.CreateClient().GetAsync("/api/v1/links");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WithInvalidKey_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "snr_bogus");

        var response = await client.GetAsync("/api/v1/links");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateAndList_ScopesToKeyOwner()
    {
        var ownerUserId = await SeedUserAndKeyAsync("api-owner", TestKey);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestKey);

        var create = await client.PostAsJsonAsync("/api/v1/links", new CreateLinkRequest
        {
            Url = "https://example.com/api-target"
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<LinkResponse>();
        Assert.NotNull(created);
        Assert.Equal(6, created.ShortCode.Length);
        Assert.Equal("https://example.com/api-target", created.LongUrl);

        var list = await client.GetFromJsonAsync<LinkListResponse>("/api/v1/links");
        Assert.NotNull(list);
        Assert.Equal(1, list.Total);
        Assert.Contains(list.Links, l => l.ShortCode == created.ShortCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var link = await db.ShortenedUrls.SingleAsync(l => l.ShortCode == created.ShortCode);
        Assert.Equal(ownerUserId, link.OwnerUserId);
    }

    [Fact]
    public async Task CreateWithCustomSlug_AndDuplicateSlug_ReturnsConflict()
    {
        await SeedUserAndKeyAsync("api-owner", TestKey);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestKey);

        var first = await client.PostAsJsonAsync("/api/v1/links", new CreateLinkRequest
        {
            Url = "https://example.com/one",
            Slug = "api-slug"
        });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync("/api/v1/links", new CreateLinkRequest
        {
            Url = "https://example.com/two",
            Slug = "api-slug"
        });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task CreateWithoutDomain_FallsBackToOwnersDefaultDomain()
    {
        var ownerUserId = await SeedUserAndKeyAsync("api-owner", TestKey);
        await SeedDomainAsync(ownerUserId, "go.example.com", verified: true, isDefault: true);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestKey);

        var create = await client.PostAsJsonAsync("/api/v1/links", new CreateLinkRequest
        {
            Url = "https://example.com/default-target"
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<LinkResponse>();
        Assert.NotNull(created);
        Assert.Equal("go.example.com", created.Domain);
        Assert.Contains("//go.example.com/", created.ShortUrl);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var link = await db.ShortenedUrls.SingleAsync(l => l.ShortCode == created.ShortCode);
        Assert.NotNull(link.DomainId);
    }

    [Fact]
    public async Task CreateWithoutDomain_WhenNoDefaultDomain_UsesInstanceHost()
    {
        await SeedUserAndKeyAsync("api-owner", TestKey);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestKey);

        var create = await client.PostAsJsonAsync("/api/v1/links", new CreateLinkRequest
        {
            Url = "https://example.com/no-domain-target"
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<LinkResponse>();
        Assert.NotNull(created);
        Assert.Null(created.Domain);
    }

    [Fact]
    public async Task Update_ChangesUrlSlugAndDomain()
    {
        var ownerUserId = await SeedUserAndKeyAsync("api-owner", TestKey);
        await SeedDomainAsync(ownerUserId, "go.example.com", verified: true);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestKey);

        var create = await client.PostAsJsonAsync("/api/v1/links", new CreateLinkRequest
        {
            Url = "https://example.com/before",
            Slug = "before"
        });
        var created = await create.Content.ReadFromJsonAsync<LinkResponse>();
        Assert.NotNull(created);

        var update = await client.PutAsJsonAsync($"/api/v1/links/before", new UpdateLinkRequest
        {
            Url = "https://example.com/after",
            Slug = "after",
            Domain = "go.example.com"
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await update.Content.ReadFromJsonAsync<LinkResponse>();
        Assert.NotNull(updated);
        Assert.Equal("after", updated.ShortCode);
        Assert.Equal("https://example.com/after", updated.LongUrl);
        Assert.Equal("go.example.com", updated.Domain);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Null(await db.ShortenedUrls.FirstOrDefaultAsync(l => l.ShortCode == "before"));
        var link = await db.ShortenedUrls.SingleAsync(l => l.ShortCode == "after");
        Assert.Equal("https://example.com/after", link.LongUrl);
        Assert.NotNull(link.DomainId);
    }

    [Fact]
    public async Task Update_UnknownDomain_ReturnsValidationError()
    {
        await SeedUserAndKeyAsync("api-owner", TestKey);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestKey);

        var create = await client.PostAsJsonAsync("/api/v1/links", new CreateLinkRequest
        {
            Url = "https://example.com/x",
            Slug = "update-domain"
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var update = await client.PutAsJsonAsync("/api/v1/links/update-domain", new UpdateLinkRequest
        {
            Domain = "not-verified.example.com"
        });
        Assert.Equal(HttpStatusCode.BadRequest, update.StatusCode);
        var problem = await update.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>();
        Assert.NotNull(problem);
        Assert.True(problem.ContainsKey("errors"));
        Assert.True(problem["errors"].ValueKind == JsonValueKind.Object);
    }

    [Fact]
    public async Task GetAndDelete_ReturnNotFound_WhenScopedToAnotherOwner()
    {
        await SeedUserAndKeyAsync("api-owner", TestKey);
        var otherOwner = await SeedUserAndKeyAsync("other-owner", "snr_otherkey1234567890abcdef12345678");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ShortenedUrls.Add(new ShortenedUrl
            {
                LongUrl = "https://example.com/other-owner",
                ShortCode = "oth123",
                OwnerUserId = otherOwner,
                CreatedAtUtc = DateTime.UtcNow
            });
            db.SaveChanges();
        }

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestKey);

        var get = await client.GetAsync("/api/v1/links/oth123");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync("/api/v1/links/oth123")).StatusCode);
    }

    [Fact]
    public async Task GetClicks_ReturnsPaginatedClickEvents()
    {
        var ownerUserId = await SeedUserAndKeyAsync("api-owner", TestKey);
        var linkId = await SeedLinkAsync(ownerUserId, "click001");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ClickEvents.AddRange(
                new ClickEvent { ShortenedUrlId = linkId, ClickedAtUtc = DateTime.UtcNow.AddMinutes(-2) },
                new ClickEvent { ShortenedUrlId = linkId, ClickedAtUtc = DateTime.UtcNow.AddMinutes(-1) });
            db.SaveChanges();
        }

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestKey);

        var response = await client.GetFromJsonAsync<ClickListResponse>("/api/v1/links/click001/clicks?pageSize=1");
        Assert.NotNull(response);
        Assert.Equal(2, response.Total);
        Assert.Single(response.Clicks);
    }

    [Fact]
    public async Task RevokedKey_ReturnsUnauthorized()
    {
        var ownerUserId = await SeedUserAndKeyAsync("api-owner", TestKey);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var key = await db.ApiKeys.SingleAsync(k => k.OwnerUserId == ownerUserId);
            key.RevokedAt = DateTime.UtcNow;
            db.SaveChanges();
        }

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestKey);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/links")).StatusCode);
    }

    [Fact]
    public async Task WhenAuthDisabled_AlwaysUnauthorized()
    {
        using var noAuthFactory = new ShortnrWebAppFactory(authEnabled: false);
        var client = noAuthFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestKey);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/links")).StatusCode);
    }

    [Fact]
    public async Task ExceedingMinuteBurstLimit_Returns429()
    {
        await SeedUserAndKeyAsync("api-owner", TestKey);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestKey);

        HttpStatusCode last = default;
        for (var i = 0; i < 61; i++)
            last = (await client.GetAsync("/api/v1/links")).StatusCode;

        Assert.Equal(HttpStatusCode.TooManyRequests, last);
    }

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

    private async Task SeedDomainAsync(long ownerUserId, string hostname, bool verified, bool isDefault = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Domains.Add(new Domain
        {
            Hostname = hostname,
            OwnerUserId = ownerUserId,
            IsVerified = verified,
            IsDefault = isDefault,
            VerificationToken = "tok",
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
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
}
