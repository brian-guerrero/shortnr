using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Web.Features.Email;
using Shortnr.Web.Features.Workspaces;

namespace Shortnr.Tests.Unit.Services;

/// <summary>
/// Tests for <see cref="WorkspaceAuthorizationService"/>, the role-enforcement
/// gate for workspace resources. Uses a real SQLite in-memory database so the
/// workspace/membership FK and unique constraints behave as in production.
/// </summary>
public class WorkspaceAuthorizationServiceTests : IDisposable
{
    private const string TestIssuer = "http://test.issuer";

    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly WorkspaceAuthorizationService _sut;
    private readonly WorkspaceService _workspaceService;

    public WorkspaceAuthorizationServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options);
        _db.Database.EnsureCreated();

        _workspaceService = new WorkspaceService(_db, BuildEmailService(), new HttpContextAccessor());
        _sut = new WorkspaceAuthorizationService(_workspaceService);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Link permissions by role
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(WorkspaceRole.Owner)]
    [InlineData(WorkspaceRole.Editor)]
    public async Task OwnerAndEditor_CanCreateEditDeleteLinks(WorkspaceRole role)
    {
        var (workspace, user) = await SeedWorkspaceWithRoleAsync(role);

        Assert.True(await _sut.CanCreateLinkAsync(workspace.Id, user.Id));
        Assert.True(await _sut.CanEditLinkAsync(workspace.Id, user.Id));
        Assert.True(await _sut.CanDeleteLinkAsync(workspace.Id, user.Id));
    }

    [Fact]
    public async Task Viewer_CanViewButCannotCreateEditDeleteLinks()
    {
        var (workspace, viewer) = await SeedWorkspaceWithRoleAsync(WorkspaceRole.Viewer);

        Assert.True(await _sut.CanViewLinksAsync(workspace.Id, viewer.Id));
        Assert.False(await _sut.CanCreateLinkAsync(workspace.Id, viewer.Id));
        Assert.False(await _sut.CanEditLinkAsync(workspace.Id, viewer.Id));
        Assert.False(await _sut.CanDeleteLinkAsync(workspace.Id, viewer.Id));
    }

    // -------------------------------------------------------------------------
    // Member-management and deletion are Owner-only
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(WorkspaceRole.Owner)]
    [InlineData(WorkspaceRole.Editor)]
    [InlineData(WorkspaceRole.Viewer)]
    public async Task OnlyOwner_CanManageMembersAndDeleteWorkspace(WorkspaceRole role)
    {
        var (workspace, user) = await SeedWorkspaceWithRoleAsync(role);

        Assert.Equal(role == WorkspaceRole.Owner, await _sut.CanManageMembersAsync(workspace.Id, user.Id));
        Assert.Equal(role == WorkspaceRole.Owner, await _sut.CanDeleteWorkspaceAsync(workspace.Id, user.Id));
    }

    // -------------------------------------------------------------------------
    // Cross-workspace access
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MemberOfWorkspaceA_CannotTouchWorkspaceB()
    {
        var (workspaceA, ownerA) = await SeedWorkspaceWithOwnerAsync("owner-a");
        var (workspaceB, _) = await SeedWorkspaceWithOwnerAsync("owner-b");

        // A user who is an owner of A must have zero authority inside B.
        Assert.False(await _sut.CanCreateLinkAsync(workspaceB.Id, ownerA.Id));
        Assert.False(await _sut.CanEditLinkAsync(workspaceB.Id, ownerA.Id));
        Assert.False(await _sut.CanDeleteLinkAsync(workspaceB.Id, ownerA.Id));
        Assert.False(await _sut.CanViewLinksAsync(workspaceB.Id, ownerA.Id));
        Assert.False(await _sut.CanManageMembersAsync(workspaceB.Id, ownerA.Id));
        Assert.False(await _sut.CanDeleteWorkspaceAsync(workspaceB.Id, ownerA.Id));
        Assert.True(await _sut.CanManageMembersAsync(workspaceA.Id, ownerA.Id));
    }

    [Fact]
    public async Task PendingMember_HasNoPermissions()
    {
        var (workspace, owner) = await SeedWorkspaceWithOwnerAsync("owner");
        // A pending invite (User 0 / not yet joined) has no active membership.
        var stranger = new User
        {
            Issuer = TestIssuer,
            Subject = "stranger",
            Email = "stranger@example.com",
            CreatedAtUtc = DateTime.UtcNow,
            LastLoginAtUtc = DateTime.UtcNow
        };
        _db.Users.Add(stranger);
        await _db.SaveChangesAsync();
        _db.WorkspaceMembers.Add(new WorkspaceMember
        {
            WorkspaceId = workspace.Id,
            UserId = stranger.Id,
            Role = WorkspaceRole.Editor,
            InviteEmail = stranger.Email,
            InvitedAtUtc = DateTime.UtcNow,
            JoinedAtUtc = null
        });
        await _db.SaveChangesAsync();

        Assert.False(await _sut.CanCreateLinkAsync(workspace.Id, stranger.Id));
        Assert.False(await _sut.CanViewLinksAsync(workspace.Id, stranger.Id));
        Assert.False(await _sut.CanManageMembersAsync(workspace.Id, stranger.Id));
        Assert.False(await _sut.CanDeleteWorkspaceAsync(workspace.Id, stranger.Id));
    }

    [Fact]
    public async Task NonMember_IsBlockedEverywhere()
    {
        var (workspace, _) = await SeedWorkspaceWithOwnerAsync("owner");
        var outsider = new User
        {
            Issuer = TestIssuer,
            Subject = "outsider",
            Email = "outsider@example.com",
            CreatedAtUtc = DateTime.UtcNow,
            LastLoginAtUtc = DateTime.UtcNow
        };
        _db.Users.Add(outsider);
        await _db.SaveChangesAsync();

        Assert.False(await _sut.CanCreateLinkAsync(workspace.Id, outsider.Id));
        Assert.False(await _sut.CanEditLinkAsync(workspace.Id, outsider.Id));
        Assert.False(await _sut.CanDeleteLinkAsync(workspace.Id, outsider.Id));
        Assert.False(await _sut.CanViewLinksAsync(workspace.Id, outsider.Id));
        Assert.False(await _sut.CanManageMembersAsync(workspace.Id, outsider.Id));
        Assert.False(await _sut.CanDeleteWorkspaceAsync(workspace.Id, outsider.Id));
    }

    [Fact]
    public async Task NullWorkspaceOrUser_ReturnsFalse()
    {
        Assert.False(await _sut.CanCreateLinkAsync(null, null));
        Assert.False(await _sut.CanEditLinkAsync(null, 1));
        Assert.False(await _sut.CanDeleteLinkAsync(1, null));
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<(Workspace Workspace, User User)> SeedWorkspaceWithRoleAsync(WorkspaceRole role)
    {
        var (workspace, owner) = await SeedWorkspaceWithOwnerAsync("owner");
        var user = new User
        {
            Issuer = TestIssuer,
            Subject = $"user-{role}",
            Email = $"user-{role}@example.com",
            CreatedAtUtc = DateTime.UtcNow,
            LastLoginAtUtc = DateTime.UtcNow
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        _db.WorkspaceMembers.Add(new WorkspaceMember
        {
            WorkspaceId = workspace.Id,
            UserId = user.Id,
            Role = role,
            InviteEmail = user.Email,
            InvitedAtUtc = DateTime.UtcNow,
            JoinedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        return (workspace, user);
    }

    private async Task<(Workspace Workspace, User Owner)> SeedWorkspaceWithOwnerAsync(string subject)
    {
        var owner = new User
        {
            Issuer = TestIssuer,
            Subject = subject,
            Email = $"{subject}@example.com",
            CreatedAtUtc = DateTime.UtcNow,
            LastLoginAtUtc = DateTime.UtcNow
        };
        _db.Users.Add(owner);
        await _db.SaveChangesAsync();
        var workspace = await _workspaceService.CreateAsync("Acme", subject, owner.Id);
        return (workspace, owner);
    }

    private static EmailService BuildEmailService() =>
        new(Options.Create(new SmtpOptions { Host = "127.0.0.1", Port = 1 }),
            NullLogger<EmailService>.Instance);
}
