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
/// Verifies the PRD-024 link lifecycle surface on /api/v1: PATCH edits
/// (URL, slug, tags, title, description), archive/unarchive, and transfer
/// between workspaces, including membership enforcement and click-count
/// preservation.
/// </summary>
public class ApiV1LinkLifecycleTests : IAsyncLifetime
{
    private const string TestKey = "snr_testkey1234567890abcdef1234567890";
    private readonly ShortnrWebAppFactory _factory = new(authEnabled: true);

    private long _ownerUserId;
    private Workspace _acme = null!;
    private Workspace _globex = null!;

    public async Task InitializeAsync()
    {
        _ownerUserId = await SeedUserAndKeyAsync("api-owner", TestKey);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            _acme = new Workspace { Name = "Acme", Slug = "acme", OwnerUserId = _ownerUserId, CreatedAtUtc = DateTime.UtcNow };
            _globex = new Workspace { Name = "Globex", Slug = "globex", OwnerUserId = _ownerUserId, CreatedAtUtc = DateTime.UtcNow };
            db.Workspaces.AddRange(_acme, _globex);
            await db.SaveChangesAsync();

            db.WorkspaceMembers.AddRange(
                new WorkspaceMember { WorkspaceId = _acme.Id, UserId = _ownerUserId, Role = WorkspaceRole.Owner, InvitedAtUtc = DateTime.UtcNow, JoinedAtUtc = DateTime.UtcNow },
                new WorkspaceMember { WorkspaceId = _globex.Id, UserId = _ownerUserId, Role = WorkspaceRole.Owner, InvitedAtUtc = DateTime.UtcNow, JoinedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
    }

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    private HttpClient AuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestKey);
        return client;
    }

    // -------------------------------------------------------------------------
    // PATCH: URL, slug, tags, title, description
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Patch_UpdatesUrl_PreservesClickCountAndCreatedAt()
    {
        var client = AuthenticatedClient();
        var linkId = await SeedLinkAsync(_ownerUserId, "patch001", clickCount: 42);

        var response = await client.PatchAsJsonAsync("/api/v1/links/patch001", new UpdateLinkRequest
        {
            Url = "https://example.com/after"
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<LinkResponse>();
        Assert.NotNull(body);
        Assert.Equal("https://example.com/after", body.LongUrl);
        Assert.Equal("patch001", body.ShortCode);
        Assert.Equal(42, body.ClickCount);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var link = await db.ShortenedUrls.SingleAsync(l => l.Id == linkId);
        Assert.Equal(42, link.ClickCount);
        Assert.NotNull(link.UpdatedAtUtc);
        Assert.Equal(link.CreatedAtUtc, body.CreatedAtUtc);
    }

    [Fact]
    public async Task Patch_UpdatesSlug_OldCodeNoLongerResolves()
    {
        await SeedLinkAsync(_ownerUserId, "patch002");
        var client = AuthenticatedClient();

        var response = await client.PatchAsJsonAsync("/api/v1/links/patch002", new UpdateLinkRequest
        {
            Slug = "patch002-renamed"
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LinkResponse>();
        Assert.NotNull(body);
        Assert.Equal("patch002-renamed", body.ShortCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Null(await db.ShortenedUrls.FirstOrDefaultAsync(l => l.ShortCode == "patch002"));
        Assert.NotNull(await db.ShortenedUrls.FirstOrDefaultAsync(l => l.ShortCode == "patch002-renamed"));
    }

    [Fact]
    public async Task Patch_ToExistingSlug_ReturnsConflict()
    {
        await SeedLinkAsync(_ownerUserId, "patch003");
        await SeedLinkAsync(_ownerUserId, "patch004");
        var client = AuthenticatedClient();

        var response = await client.PatchAsJsonAsync("/api/v1/links/patch003", new UpdateLinkRequest
        {
            Slug = "patch004"
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Patch_InvalidUrl_ReturnsValidationError()
    {
        await SeedLinkAsync(_ownerUserId, "patch005");
        var client = AuthenticatedClient();

        var response = await client.PatchAsJsonAsync("/api/v1/links/patch005", new UpdateLinkRequest
        {
            Url = "not-a-url"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_InvalidSlug_ReturnsValidationError()
    {
        await SeedLinkAsync(_ownerUserId, "patch006");
        var client = AuthenticatedClient();

        var response = await client.PatchAsJsonAsync("/api/v1/links/patch006", new UpdateLinkRequest
        {
            Slug = "has spaces"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_UnknownLink_ReturnsNotFound()
    {
        var client = AuthenticatedClient();

        var response = await client.PatchAsJsonAsync("/api/v1/links/patch-nope", new UpdateLinkRequest
        {
            Url = "https://example.com/whatever"
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Patch_SetsTitleAndDescription()
    {
        var linkId = await SeedLinkAsync(_ownerUserId, "patch007");
        var client = AuthenticatedClient();

        var response = await client.PatchAsJsonAsync("/api/v1/links/patch007", new UpdateLinkRequest
        {
            Title = "Q2 campaign",
            Description = "Summer promo landing page"
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LinkResponse>();
        Assert.NotNull(body);
        Assert.Equal("Q2 campaign", body.Title);
        Assert.Equal("Summer promo landing page", body.Description);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var link = await db.ShortenedUrls.SingleAsync(l => l.Id == linkId);
        Assert.Equal("Q2 campaign", link.Title);
        Assert.Equal("Summer promo landing page", link.Description);
    }

    [Fact]
    public async Task Patch_ReplacesTags()
    {
        await SeedLinkAsync(_ownerUserId, "patch008", tags: ["newsletter", "old-tag"]);
        var client = AuthenticatedClient();

        var response = await client.PatchAsJsonAsync("/api/v1/links/patch008", new UpdateLinkRequest
        {
            Tags = new[] { "newsletter", "q2" }
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LinkResponse>();
        Assert.NotNull(body);
        Assert.Equal(["newsletter", "q2"], body.Tags);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var linkId = await db.ShortenedUrls.Where(l => l.ShortCode == "patch008").Select(l => l.Id).SingleAsync();
        var names = await db.ShortenedUrlTags.Where(t => t.ShortenedUrlId == linkId).Select(t => t.Name).ToListAsync();
        Assert.Equal(["newsletter", "q2"], names.OrderBy(n => n));
    }

    [Fact]
    public async Task Patch_OnWorkspaceLink_RequiresMembership()
    {
        var otherKey = "snr_otherkey1234567890abcdef1234567890";
        var otherOwner = await SeedUserAndKeyAsync("other-owner", otherKey);

        long otherWorkspaceId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var otherWorkspace = new Workspace { Name = "Rival", Slug = "rival", OwnerUserId = otherOwner, CreatedAtUtc = DateTime.UtcNow };
            db.Workspaces.Add(otherWorkspace);
            await db.SaveChangesAsync();
            otherWorkspaceId = otherWorkspace.Id;
            db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = otherWorkspace.Id, UserId = otherOwner, Role = WorkspaceRole.Owner, InvitedAtUtc = DateTime.UtcNow, JoinedAtUtc = DateTime.UtcNow });
            db.ShortenedUrls.Add(new ShortenedUrl { LongUrl = "https://rival.com/x", ShortCode = "patch009", WorkspaceId = otherWorkspace.Id, CreatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var client = AuthenticatedClient();
        var response = await client.PatchAsJsonAsync("/api/v1/links/patch009?workspace=rival", new UpdateLinkRequest
        {
            Url = "https://example.com/hijack"
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Archive / unarchive
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Archive_SetsArchivedAtUtc()
    {
        var linkId = await SeedLinkAsync(_ownerUserId, "arch001", clickCount: 7);
        var client = AuthenticatedClient();

        var response = await client.PostAsync("/api/v1/links/arch001/archive", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LinkResponse>();
        Assert.NotNull(body);
        Assert.NotNull(body.ArchivedAtUtc);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var link = await db.ShortenedUrls.SingleAsync(l => l.Id == linkId);
        Assert.NotNull(link.ArchivedAtUtc);
        Assert.Equal(7, link.ClickCount);
    }

    [Fact]
    public async Task Archive_AlreadyArchived_IsNoOp()
    {
        var linkId = await SeedLinkAsync(_ownerUserId, "arch002");
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var link = await db.ShortenedUrls.SingleAsync(l => l.Id == linkId);
            link.ArchivedAtUtc = DateTime.UtcNow.AddDays(-1);
            await db.SaveChangesAsync();
        }

        var client = AuthenticatedClient();
        var response = await client.PostAsync("/api/v1/links/arch002/archive", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope2 = _factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var link2 = await db2.ShortenedUrls.SingleAsync(l => l.Id == linkId);
        Assert.NotNull(link2.ArchivedAtUtc);
        Assert.Equal(link2.ArchivedAtUtc!.Value, DateTime.UtcNow.AddDays(-1), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Unarchive_ClearsArchivedAtUtc()
    {
        var linkId = await SeedLinkAsync(_ownerUserId, "arch003");
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var link = await db.ShortenedUrls.SingleAsync(l => l.Id == linkId);
            link.ArchivedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var client = AuthenticatedClient();
        var response = await client.PostAsync("/api/v1/links/arch003/unarchive", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LinkResponse>();
        Assert.NotNull(body);
        Assert.Null(body.ArchivedAtUtc);

        using var scope2 = _factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var link2 = await db2.ShortenedUrls.SingleAsync(l => l.Id == linkId);
        Assert.Null(link2.ArchivedAtUtc);
    }

    [Fact]
    public async Task Unarchive_ActiveLink_IsNoOp()
    {
        var linkId = await SeedLinkAsync(_ownerUserId, "arch004");
        var client = AuthenticatedClient();

        var response = await client.PostAsync("/api/v1/links/arch004/unarchive", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var link = await db.ShortenedUrls.SingleAsync(l => l.Id == linkId);
        Assert.Null(link.ArchivedAtUtc);
    }

    [Fact]
    public async Task Archive_UnknownLink_ReturnsNotFound()
    {
        var client = AuthenticatedClient();

        var response = await client.PostAsync("/api/v1/links/arch-nope/archive", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Unarchive_UnknownLink_ReturnsNotFound()
    {
        var client = AuthenticatedClient();

        var response = await client.PostAsync("/api/v1/links/arch-nope/unarchive", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Transfer
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Transfer_PersonalLinkToWorkspace_MovesLinkAndPreservesClicks()
    {
        var linkId = await SeedLinkAsync(_ownerUserId, "trx001", clickCount: 12);
        var client = AuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/v1/links/trx001/transfer", new TransferLinkRequest { Workspace = "acme" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LinkResponse>();
        Assert.NotNull(body);
        Assert.Equal("acme", body.Workspace);
        Assert.Equal(12, body.ClickCount);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var link = await db.ShortenedUrls.SingleAsync(l => l.Id == linkId);
        Assert.Equal(_acme.Id, link.WorkspaceId);
        Assert.Null(link.OwnerUserId);
        Assert.Equal(12, link.ClickCount);
    }

    [Fact]
    public async Task Transfer_WorkspaceToWorkspace_MovesLinkAndPreservesClicks()
    {
        var linkId = await SeedWorkspaceLinkAsync(_acme.Id, "trx002", clickCount: 5);
        var client = AuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/v1/links/trx002/transfer?workspace=acme", new TransferLinkRequest { Workspace = "globex" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LinkResponse>();
        Assert.NotNull(body);
        Assert.Equal("globex", body.Workspace);
        Assert.Equal(5, body.ClickCount);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var link = await db.ShortenedUrls.SingleAsync(l => l.Id == linkId);
        Assert.Equal(_globex.Id, link.WorkspaceId);
        Assert.Equal(5, link.ClickCount);
    }

    [Fact]
    public async Task Transfer_ToWorkspaceWithoutMembership_Returns403()
    {
        var otherKey = "snr_otherkey1234567890abcdef1234567890";
        var otherOwner = await SeedUserAndKeyAsync("other-owner", otherKey);

        long rivalWorkspaceId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var rival = new Workspace { Name = "Rival", Slug = "rival", OwnerUserId = otherOwner, CreatedAtUtc = DateTime.UtcNow };
            db.Workspaces.Add(rival);
            await db.SaveChangesAsync();
            rivalWorkspaceId = rival.Id;
            db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = rival.Id, UserId = otherOwner, Role = WorkspaceRole.Owner, InvitedAtUtc = DateTime.UtcNow, JoinedAtUtc = DateTime.UtcNow });
            db.ShortenedUrls.Add(new ShortenedUrl { LongUrl = "https://rival.com/y", ShortCode = "trx003", WorkspaceId = rival.Id, CreatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var client = AuthenticatedClient();
        var response = await client.PostAsJsonAsync("/api/v1/links/trx003/transfer?workspace=rival", new TransferLinkRequest { Workspace = "acme" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Transfer_ToWorkspaceTheCallerIsNotMemberOf_Returns403()
    {
        var otherKey = "snr_otherkey1234567890abcdef1234567890";
        await SeedUserAndKeyAsync("other-owner", otherKey);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // _ownerUserId owns a personal link but is NOT a member of 'rival'.
            var rival = new Workspace { Name = "Rival", Slug = "rival", OwnerUserId = _ownerUserId, CreatedAtUtc = DateTime.UtcNow };
            db.Workspaces.Add(rival);
            await db.SaveChangesAsync();
            db.ShortenedUrls.Add(new ShortenedUrl { LongUrl = "https://example.com/trx", ShortCode = "trx004", OwnerUserId = _ownerUserId, CreatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var client = AuthenticatedClient();
        var response = await client.PostAsJsonAsync("/api/v1/links/trx004/transfer", new TransferLinkRequest { Workspace = "rival" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Transfer_InvalidWorkspaceSlug_ReturnsValidationError()
    {
        await SeedLinkAsync(_ownerUserId, "trx005");
        var client = AuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/v1/links/trx005/transfer", new TransferLinkRequest { Workspace = "not a slug!" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Transfer_UnknownLink_ReturnsNotFound()
    {
        var client = AuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/v1/links/trx-nope/transfer", new TransferLinkRequest { Workspace = "acme" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // List response exposes lifecycle fields
    // -------------------------------------------------------------------------

    [Fact]
    public async Task List_ExposesLifecycleFields()
    {
        await SeedLinkAsync(_ownerUserId, "list001", tags: ["a"], title: "T", description: "D");
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var link = await db.ShortenedUrls.SingleAsync(l => l.ShortCode == "list001");
            link.ArchivedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var client = AuthenticatedClient();
        var list = await client.GetFromJsonAsync<LinkListResponse>("/api/v1/links");
        Assert.NotNull(list);
        var row = list.Links.Single(l => l.ShortCode == "list001");
        Assert.Equal(["a"], row.Tags);
        Assert.Equal("T", row.Title);
        Assert.Equal("D", row.Description);
        Assert.NotNull(row.ArchivedAtUtc);
    }

    // -------------------------------------------------------------------------
    // Backward compatibility
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Put_StillUpdatesUrlAndSlug()
    {
        await SeedLinkAsync(_ownerUserId, "put001");
        var client = AuthenticatedClient();

        var response = await client.PutAsJsonAsync("/api/v1/links/put001", new UpdateLinkRequest
        {
            Url = "https://example.com/put-after",
            Slug = "put001-after"
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LinkResponse>();
        Assert.NotNull(body);
        Assert.Equal("put001-after", body.ShortCode);
        Assert.Equal("https://example.com/put-after", body.LongUrl);
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

    private async Task<long> SeedLinkAsync(long ownerUserId, string shortCode, long clickCount = 0, IReadOnlyList<string>? tags = null, string? title = null, string? description = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var link = new ShortenedUrl
        {
            LongUrl = "https://example.com/seed",
            ShortCode = shortCode,
            OwnerUserId = ownerUserId,
            ClickCount = clickCount,
            Title = title,
            Description = description,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.ShortenedUrls.Add(link);
        await db.SaveChangesAsync();

        if (tags is { Count: > 0 })
        {
            db.ShortenedUrlTags.AddRange(tags.Select(t => new ShortenedUrlTag
            {
                ShortenedUrlId = link.Id,
                Name = t,
                CreatedAtUtc = DateTime.UtcNow
            }));
            await db.SaveChangesAsync();
        }

        return link.Id;
    }

    private async Task<long> SeedWorkspaceLinkAsync(long workspaceId, string shortCode, long clickCount = 0)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var link = new ShortenedUrl
        {
            LongUrl = "https://example.com/ws",
            ShortCode = shortCode,
            WorkspaceId = workspaceId,
            ClickCount = clickCount,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.ShortenedUrls.Add(link);
        await db.SaveChangesAsync();
        return link.Id;
    }
}
