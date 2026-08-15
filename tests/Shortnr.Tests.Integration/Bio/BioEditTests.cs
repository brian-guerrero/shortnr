using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Tests.Integration.Infrastructure;

namespace Shortnr.Tests.Integration.Bio;

/// <summary>
/// Verifies the /bio/edit management page: page creation and validation, settings
/// updates, adding/removing/reordering/toggling links, owner scoping and auth gating.
/// </summary>
public class BioEditTests : IAsyncLifetime
{
    private static readonly Regex AntiforgeryTokenRegex = new(
        @"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""",
        RegexOptions.Compiled);

    private readonly ShortnrWebAppFactory _factory = new(authEnabled: true);

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    [Fact]
    public async Task CreateBioPage_StoresFields()
    {
        var ownerId = await SetAuthenticatedUserAndSeedUserAsync("alice");
        var client = _factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/bio/edit?handler=Create", token,
            ("slug", "alicebio"), ("displayName", "Alice's Corner"), ("bioText", "hey there"), ("theme", "ocean"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("/bio/alicebio", body);
        Assert.Contains("Theme", body);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var page = await db.BioPages.SingleAsync();
        Assert.Equal(ownerId, page.OwnerUserId);
        Assert.Equal("alicebio", page.Slug);
        Assert.Equal("Alice's Corner", page.DisplayName);
        Assert.Equal("hey there", page.BioText);
        Assert.Equal("ocean", page.Theme);
    }

    [Fact]
    public async Task CreateBioPage_InvalidSlug_ShowsError()
    {
        await SetAuthenticatedUserAndSeedUserAsync("alice");
        var client = _factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/bio/edit?handler=Create", token,
            ("slug", "bad slug!"), ("displayName", "Alice"));

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Bio page code must be", body);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await db.BioPages.CountAsync());
    }

    [Fact]
    public async Task CreateBioPage_SlugCollision_ShowsError()
    {
        await SetAuthenticatedUserAndSeedUserAsync("alice");
        var ownerId = await SeedUserAsync("bob");
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.BioPages.Add(new BioPage
            {
                OwnerUserId = ownerId,
                Slug = "taken",
                DisplayName = "Bob",
                Theme = "default",
                CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/bio/edit?handler=Create", token,
            ("slug", "taken"), ("displayName", "Alice"));

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("already taken", body);

        using var verifyScope = _factory.Services.CreateScope();
        var db2 = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db2.BioPages.CountAsync());
    }

    [Fact]
    public async Task CreateBioPage_SecondPage_ShowsError()
    {
        var ownerId = await SetAuthenticatedUserAndSeedUserAsync("alice");
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.BioPages.Add(new BioPage
            {
                OwnerUserId = ownerId,
                Slug = "first",
                DisplayName = "Alice",
                Theme = "default",
                CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/bio/edit?handler=Create", token,
            ("slug", "second"), ("displayName", "Alice"));

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("already have a bio page", body);
    }

    [Fact]
    public async Task UpdateBioPage_ChangesSettings()
    {
        var ownerId = await SetAuthenticatedUserAndSeedUserAsync("alice");
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.BioPages.Add(new BioPage
            {
                OwnerUserId = ownerId,
                Slug = "alicebio",
                DisplayName = "Alice",
                Theme = "default",
                CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/bio/edit?handler=Update", token,
            ("displayName", "Alice Updated"), ("bioText", "new bio"), ("theme", "sunset"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Alice Updated", body);

        using var verifyScope = _factory.Services.CreateScope();
        var db2 = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var page = await db2.BioPages.SingleAsync();
        Assert.Equal("Alice Updated", page.DisplayName);
        Assert.Equal("new bio", page.BioText);
        Assert.Equal("sunset", page.Theme);
    }

    [Fact]
    public async Task AddExistingLink_AddsToBioPage()
    {
        var ownerId = await SetAuthenticatedUserAndSeedUserAsync("alice");
        var pageId = await SeedBioPageAsync(ownerId, "alicebio");
        var linkId = await SeedLinkAsync(ownerId, "abc123", "https://example.com/one");
        var client = _factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/bio/edit?handler=AddLink", token,
            ("linkId", linkId.ToString()), ("title", "My Link"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("My Link", body);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entry = await db.BioPageLinks.SingleAsync();
        Assert.Equal(pageId, entry.BioPageId);
        Assert.Equal(linkId, entry.ShortenedUrlId);
        Assert.Equal("My Link", entry.Title);
        Assert.True(entry.IsVisible);
        Assert.Equal(0, entry.SortOrder);
    }

    [Fact]
    public async Task AddExistingLink_NoTitle_DefaultsToLinkTitle()
    {
        var ownerId = await SetAuthenticatedUserAndSeedUserAsync("alice");
        await SeedBioPageAsync(ownerId, "alicebio");
        var linkId = await SeedLinkAsync(ownerId, "abc123", "https://example.com/one");
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.ShortenedUrls.SingleAsync(l => l.Id == linkId)).Title = "Dashboard Title";
            await db.SaveChangesAsync();
        }
        var client = _factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/bio/edit?handler=AddLink", token,
            ("linkId", linkId.ToString()), ("title", ""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entry = await verifyDb.BioPageLinks.SingleAsync();
        Assert.Equal("Dashboard Title", entry.Title);
    }

    [Fact]
    public async Task AddExistingLink_NoTitleNoLinkTitle_DefaultsToShortCode()
    {
        var ownerId = await SetAuthenticatedUserAndSeedUserAsync("alice");
        await SeedBioPageAsync(ownerId, "alicebio");
        var linkId = await SeedLinkAsync(ownerId, "abc123", "https://example.com/one");
        var client = _factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/bio/edit?handler=AddLink", token,
            ("linkId", linkId.ToString()), ("title", ""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entry = await db.BioPageLinks.SingleAsync();
        Assert.Equal("abc123", entry.Title);
    }

    [Fact]
    public async Task AddNewUrl_CreatesLinkAndAddsToBioPage()
    {
        var ownerId = await SetAuthenticatedUserAndSeedUserAsync("alice");
        var pageId = await SeedBioPageAsync(ownerId, "alicebio");
        var client = _factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/bio/edit?handler=AddLink", token,
            ("newUrl", "https://example.com/new"), ("title", "Fresh Link"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Fresh Link", body);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var link = await db.ShortenedUrls.SingleAsync(l => l.LongUrl == "https://example.com/new");
        Assert.Equal(ownerId, link.OwnerUserId);
        Assert.Null(link.DomainId);
        var entry = await db.BioPageLinks.SingleAsync();
        Assert.Equal(pageId, entry.BioPageId);
        Assert.Equal(link.Id, entry.ShortenedUrlId);
        Assert.Equal("Fresh Link", entry.Title);
    }

    [Fact]
    public async Task AddNewUrl_DuplicateLink_Rejected()
    {
        var ownerId = await SetAuthenticatedUserAndSeedUserAsync("alice");
        var pageId = await SeedBioPageAsync(ownerId, "alicebio");
        await SeedLinkAsync(ownerId, "abc123", "https://example.com/one");
        var client = _factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var first = await PostFormAsync(client, "/bio/edit?handler=AddLink", token,
            ("linkId", (await FindLinkIdAsync(ownerId, "abc123")).ToString()), ("title", "One"));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await PostFormAsync(client, "/bio/edit?handler=AddLink", token,
            ("linkId", (await FindLinkIdAsync(ownerId, "abc123")).ToString()), ("title", "Two"));

        var body = await second.Content.ReadAsStringAsync();
        Assert.Contains("already on your bio page", body);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.BioPageLinks.CountAsync());
        _ = pageId;
    }

    [Fact]
    public async Task AddExistingLink_FromAnotherOwner_Rejected()
    {
        var ownerId = await SetAuthenticatedUserAndSeedUserAsync("alice");
        await SeedBioPageAsync(ownerId, "alicebio");
        var bobId = await SeedUserAsync("bob");
        var bobLinkId = await SeedLinkAsync(bobId, "bob000", "https://example.com/bob");
        await SeedBioPageAsync(bobId, "bobbio");
        var client = _factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/bio/edit?handler=AddLink", token,
            ("linkId", bobLinkId.ToString()));

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Link not found", body);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await db.BioPageLinks.CountAsync());
    }

    [Fact]
    public async Task RemoveLink_RemovesEntry()
    {
        var ownerId = await SetAuthenticatedUserAndSeedUserAsync("alice");
        var pageId = await SeedBioPageAsync(ownerId, "alicebio");
        var linkId = await SeedLinkAsync(ownerId, "abc123", "https://example.com/one");
        var entryId = await SeedBioPageLinkAsync(pageId, linkId, "One", 0);
        var client = _factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/bio/edit?handler=RemoveLink", token,
            ("id", entryId.ToString()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("No links yet", body);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await db.BioPageLinks.CountAsync());
    }

    [Fact]
    public async Task MoveLink_ReordersEntries()
    {
        var ownerId = await SetAuthenticatedUserAndSeedUserAsync("alice");
        var pageId = await SeedBioPageAsync(ownerId, "alicebio");
        var linkA = await SeedLinkAsync(ownerId, "aaaaaa", "https://example.com/a");
        var linkB = await SeedLinkAsync(ownerId, "bbbbbb", "https://example.com/b");
        var entryA = await SeedBioPageLinkAsync(pageId, linkA, "A", 0);
        var entryB = await SeedBioPageLinkAsync(pageId, linkB, "B", 1);
        var client = _factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/bio/edit?handler=MoveLink", token,
            ("id", entryB.ToString()), ("direction", "up"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var orders = await db.BioPageLinks
            .Where(b => b.BioPageId == pageId)
            .OrderBy(b => b.SortOrder)
            .Select(b => b.Id)
            .ToListAsync();
        Assert.Equal(new[] { entryB, entryA }, orders);
    }

    [Fact]
    public async Task ToggleLink_FlipsVisibility()
    {
        var ownerId = await SetAuthenticatedUserAndSeedUserAsync("alice");
        var pageId = await SeedBioPageAsync(ownerId, "alicebio");
        var linkId = await SeedLinkAsync(ownerId, "abc123", "https://example.com/one");
        var entryId = await SeedBioPageLinkAsync(pageId, linkId, "One", 0);
        var client = _factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/bio/edit?handler=ToggleLink", token,
            ("id", entryId.ToString()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False((await db.BioPageLinks.SingleAsync(b => b.Id == entryId)).IsVisible);
    }

    [Fact]
    public async Task UpdateLinkTitle_ChangesTitle()
    {
        var ownerId = await SetAuthenticatedUserAndSeedUserAsync("alice");
        var pageId = await SeedBioPageAsync(ownerId, "alicebio");
        var linkId = await SeedLinkAsync(ownerId, "abc123", "https://example.com/one");
        var entryId = await SeedBioPageLinkAsync(pageId, linkId, "Old title", 0);
        var client = _factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/bio/edit?handler=UpdateLinkTitle", token,
            ("id", entryId.ToString()), ("title", "New title"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("New title", body);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal("New title", (await db.BioPageLinks.SingleAsync(b => b.Id == entryId)).Title);
    }

    [Fact]
    public async Task UpdateLinkTitle_Empty_ShowsErrorAndKeepsTitle()
    {
        var ownerId = await SetAuthenticatedUserAndSeedUserAsync("alice");
        var pageId = await SeedBioPageAsync(ownerId, "alicebio");
        var linkId = await SeedLinkAsync(ownerId, "abc123", "https://example.com/one");
        var entryId = await SeedBioPageLinkAsync(pageId, linkId, "Old title", 0);
        var client = _factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/bio/edit?handler=UpdateLinkTitle", token,
            ("id", entryId.ToString()), ("title", "   "));

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Title can&#x27;t be empty", body);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal("Old title", (await db.BioPageLinks.SingleAsync(b => b.Id == entryId)).Title);
    }

    [Fact]
    public async Task AddLinkForm_ExcludesLinksAlreadyOnBioPage()
    {
        var ownerId = await SetAuthenticatedUserAndSeedUserAsync("alice");
        var pageId = await SeedBioPageAsync(ownerId, "alicebio");
        var addedLinkId = await SeedLinkAsync(ownerId, "added1", "https://example.com/added");
        var availableLinkId = await SeedLinkAsync(ownerId, "avail1", "https://example.com/available");
        await SeedBioPageLinkAsync(pageId, addedLinkId, "Already added", 0);
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/bio/edit");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain($"<option value=\"{addedLinkId}\"", body);
        Assert.Contains($"<option value=\"{availableLinkId}\"", body);
    }

    [Fact]
    public async Task BioPageIsScopedToOwner()
    {
        var bobId = await SeedUserAsync("bob");
        await SeedBioPageAsync(bobId, "bobbio");
        await SeedLinkAsync(bobId, "bob000", "https://example.com/bob");
        await SetAuthenticatedUserAndSeedUserAsync("alice");

        var response = await _factory.CreateClient().GetAsync("/bio/edit");
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("bobbio", body);
        Assert.DoesNotContain("bob000", body);
        Assert.Contains("Create bio page", body);
    }

    [Fact]
    public async Task WhenAuthEnabled_UnauthenticatedFullPage_RedirectsToIndex()
    {
        using var authFactory = new ShortnrWebAppFactory(authEnabled: true);
        var client = authFactory.CreateClientNoRedirect();

        var response = await client.GetAsync("/bio/edit");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task WhenAuthEnabled_UnauthenticatedHtmx_ReturnsUnauthorized()
    {
        using var authFactory = new ShortnrWebAppFactory(authEnabled: true);
        var client = authFactory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/bio/edit");
        request.Headers.Add("HX-Request", "true");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<long> SetAuthenticatedUserAndSeedUserAsync(string subject)
    {
        _factory.Services.GetRequiredService<TestAuthState>().SetAuthenticatedUser(subject, ShortnrWebAppFactory.TestIssuer);
        return await SeedUserAsync(subject);
    }

    private async Task<long> SeedUserAsync(string subject)
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
        return user.Id;
    }

    private async Task<long> SeedBioPageAsync(long ownerUserId, string slug)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var page = new BioPage
        {
            OwnerUserId = ownerUserId,
            Slug = slug,
            DisplayName = slug,
            Theme = "default",
            CreatedAtUtc = DateTime.UtcNow
        };
        db.BioPages.Add(page);
        await db.SaveChangesAsync();
        return page.Id;
    }

    private async Task<long> SeedLinkAsync(long ownerUserId, string shortCode, string longUrl)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var link = new ShortenedUrl
        {
            OwnerUserId = ownerUserId,
            ShortCode = shortCode,
            LongUrl = longUrl,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.ShortenedUrls.Add(link);
        await db.SaveChangesAsync();
        return link.Id;
    }

    private async Task<long> SeedBioPageLinkAsync(long bioPageId, long shortUrlId, string title, int sortOrder)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entry = new BioPageLink
        {
            BioPageId = bioPageId,
            ShortenedUrlId = shortUrlId,
            Title = title,
            SortOrder = sortOrder,
            IsVisible = true
        };
        db.BioPageLinks.Add(entry);
        await db.SaveChangesAsync();
        return entry.Id;
    }

    private async Task<long> FindLinkIdAsync(long ownerUserId, string shortCode)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.ShortenedUrls.SingleAsync(l => l.OwnerUserId == ownerUserId && l.ShortCode == shortCode)).Id;
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        var page = await client.GetAsync("/bio/edit");
        var html = await page.Content.ReadAsStringAsync();
        var match = AntiforgeryTokenRegex.Match(html);
        Assert.True(match.Success, "Antiforgery token not found in bio edit page.");
        return match.Groups[1].Value;
    }

    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string path, string token, params (string Name, string Value)[] fields)
    {
        var pairs = fields
            .Select(f => new KeyValuePair<string, string>(f.Name, f.Value))
            .ToList();
        pairs.Add(new KeyValuePair<string, string>("__RequestVerificationToken", token));
        return await client.PostAsync(path, new FormUrlEncodedContent(pairs));
    }
}
