using System.Threading.Channels;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Web.Features.Authentication;
using Shortnr.Web.Features.Email;
using Shortnr.Web.Features.Workspaces;
using System.Threading.Channels;

namespace Shortnr.Tests.Unit.Services;

/// <summary>
/// Tests for <see cref="WorkspaceService"/>.
/// <para>
/// Uses a real SQLite in-memory database (not the EF Core InMemory provider)
/// because the behaviours under test — duplicate-slug rejection, FK cascades and
/// the workspace membership unique index — are enforced by the relational
/// database and are silently ignored by the InMemory provider.
/// </para>
/// </summary>
public class WorkspaceServiceTests : IDisposable
{
    private const string TestIssuer = "http://test.issuer";

    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;

    public WorkspaceServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // CreateAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_CreatesWorkspaceAndOwnerMembership()
    {
        var owner = await SeedUserAsync("owner", "owner@example.com");
        var sut = BuildService();

        var workspace = await sut.CreateAsync("Acme", "acme", owner.Id);

        Assert.Equal("Acme", workspace.Name);
        Assert.Equal("acme", workspace.Slug);
        Assert.Equal(owner.Id, workspace.OwnerUserId);

        var member = await _db.WorkspaceMembers.SingleAsync(m => m.WorkspaceId == workspace.Id);
        Assert.Equal(owner.Id, member.UserId);
        Assert.Equal(WorkspaceRole.Owner, member.Role);
        Assert.NotNull(member.JoinedAtUtc);
    }

    [Fact]
    public async Task CreateAsync_TrimsNameAndSlug()
    {
        var owner = await SeedUserAsync("owner", "owner@example.com");
        var sut = BuildService();

        var workspace = await sut.CreateAsync("  Acme  ", "  acme  ", owner.Id);

        Assert.Equal("Acme", workspace.Name);
        Assert.Equal("acme", workspace.Slug);
    }

    [Fact]
    public async Task CreateAsync_RejectsDuplicateSlug()
    {
        var owner = await SeedUserAsync("owner", "owner@example.com");
        var sut = BuildService();
        await sut.CreateAsync("First", "acme", owner.Id);

        // The unique index on Workspace.Slug is the enforcement point; a second
        // workspace reusing the slug must fail at the database.
        await Assert.ThrowsAsync<DbUpdateException>(() =>
            sut.CreateAsync("Second", "acme", owner.Id));
    }

    [Theory]
    [InlineData("acme")]
    [InlineData("ac-me")]
    [InlineData("ac_me")]
    [InlineData("a1b")]
    public void IsValidSlug_ValidSlugs_ReturnTrue(string slug)
    {
        Assert.True(WorkspaceService.IsValidSlug(slug));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("-acme")]
    [InlineData("_acme")]
    [InlineData("has space")]
    [InlineData("toolongtoolongtoolongtoolongtoolongtoolongtoolongtoolong")]
    public void IsValidSlug_InvalidSlugs_ReturnFalse(string slug)
    {
        Assert.False(WorkspaceService.IsValidSlug(slug));
    }

    // -------------------------------------------------------------------------
    // InviteMemberAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task InviteMemberAsync_UnknownEmail_CreatesPendingMember()
    {
        var (workspace, owner) = await SeedWorkspaceWithOwnerAsync();
        var sut = BuildService();

        var invited = await sut.InviteMemberAsync(workspace.Id, "newbie@example.com", WorkspaceRole.Editor, owner.Id);

        Assert.True(invited);
        var member = await _db.WorkspaceMembers.SingleAsync(m =>
            m.WorkspaceId == workspace.Id && m.InviteEmail == "newbie@example.com");
        Assert.Null(member.UserId);
        Assert.Null(member.JoinedAtUtc);
        Assert.Equal(WorkspaceRole.Editor, member.Role);
    }

    [Fact]
    public async Task InviteMemberAsync_ExistingUser_CreatesActiveMember()
    {
        var (workspace, owner) = await SeedWorkspaceWithOwnerAsync();
        var invitee = await SeedUserAsync("invitee", "invitee@example.com");
        var sut = BuildService();

        var invited = await sut.InviteMemberAsync(workspace.Id, invitee.Email!, WorkspaceRole.Viewer, owner.Id);

        Assert.True(invited);
        var member = await _db.WorkspaceMembers.SingleAsync(m =>
            m.WorkspaceId == workspace.Id && m.UserId == invitee.Id);
        Assert.Equal(invitee.Id, member.UserId);
        Assert.NotNull(member.JoinedAtUtc);
        Assert.Equal(WorkspaceRole.Viewer, member.Role);
    }

    [Fact]
    public async Task InviteMemberAsync_NonOwner_ReturnsFalseAndAddsNoMember()
    {
        var (workspace, owner) = await SeedWorkspaceWithOwnerAsync();
        var editor = await SeedUserAsync("editor", "editor@example.com");
        var sut = BuildService();
        await sut.InviteMemberAsync(workspace.Id, editor.Email!, WorkspaceRole.Editor, owner.Id);

        var invited = await sut.InviteMemberAsync(workspace.Id, "other@example.com", WorkspaceRole.Editor, editor.Id);

        Assert.False(invited);
        Assert.Equal(2, await _db.WorkspaceMembers.CountAsync(m => m.WorkspaceId == workspace.Id));
    }

    [Fact]
    public async Task InviteMemberAsync_AlreadyPending_ReturnsFalse()
    {
        var (workspace, owner) = await SeedWorkspaceWithOwnerAsync();
        var sut = BuildService();
        await sut.InviteMemberAsync(workspace.Id, "newbie@example.com", WorkspaceRole.Editor, owner.Id);

        var invited = await sut.InviteMemberAsync(workspace.Id, "NEWBIE@example.com", WorkspaceRole.Editor, owner.Id);

        Assert.False(invited);
        Assert.Equal(2, await _db.WorkspaceMembers.CountAsync(m => m.WorkspaceId == workspace.Id));
    }

    [Fact]
    public async Task InviteMemberAsync_AlreadyActiveMember_ReturnsFalse()
    {
        var (workspace, owner) = await SeedWorkspaceWithOwnerAsync();
        var invitee = await SeedUserAsync("invitee", "invitee@example.com");
        var sut = BuildService();
        await sut.InviteMemberAsync(workspace.Id, invitee.Email!, WorkspaceRole.Editor, owner.Id);

        var invited = await sut.InviteMemberAsync(workspace.Id, invitee.Email!, WorkspaceRole.Viewer, owner.Id);

        Assert.False(invited);
    }

    // -------------------------------------------------------------------------
    // Auto-accept on login (UserProvisioningProcessor)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Provisioning_NewUserWithPendingInvite_AutoAcceptsMembership()
    {
        var (workspace, _) = await SeedWorkspaceWithOwnerAsync();
        _db.WorkspaceMembers.Add(new WorkspaceMember
        {
            WorkspaceId = workspace.Id,
            UserId = null,
            Role = WorkspaceRole.Editor,
            InviteEmail = "invited@example.com",
            InvitedAtUtc = DateTime.UtcNow,
            JoinedAtUtc = null
        });
        await _db.SaveChangesAsync();

        await RunProvisioningAsync("new-user", "invited@example.com");

        var user = await _db.Users.SingleAsync(u => u.Subject == "new-user");
        using var read = CreateReadContext();
        var member = await read.WorkspaceMembers.SingleAsync(m =>
            m.WorkspaceId == workspace.Id && m.UserId == user.Id);
        Assert.Equal(WorkspaceRole.Editor, member.Role);
        Assert.NotNull(member.JoinedAtUtc);
        Assert.Null(member.InviteEmail);
    }

    [Fact]
    public async Task Provisioning_NewUserWithNoInvite_IsProvisionedButNoMembership()
    {
        await RunProvisioningAsync("loner", "loner@example.com");

        using var read = CreateReadContext();
        Assert.True(await read.Users.AnyAsync(u => u.Subject == "loner"));
        Assert.Empty(await read.WorkspaceMembers.ToListAsync());
    }

    [Fact]
    public async Task Provisioning_LoginEmailMismatch_KeepsInvitePending()
    {
        var (workspace, _) = await SeedWorkspaceWithOwnerAsync();
        _db.WorkspaceMembers.Add(new WorkspaceMember
        {
            WorkspaceId = workspace.Id,
            UserId = null,
            Role = WorkspaceRole.Editor,
            InviteEmail = "invited@example.com",
            InvitedAtUtc = DateTime.UtcNow,
            JoinedAtUtc = null
        });
        await _db.SaveChangesAsync();

        await RunProvisioningAsync("new-user", "different@example.com");

        using var read = CreateReadContext();
        var member = await read.WorkspaceMembers.SingleAsync(m =>
            m.WorkspaceId == workspace.Id && m.InviteEmail == "invited@example.com");
        Assert.Null(member.UserId);
        Assert.Null(member.JoinedAtUtc);
    }

    [Fact]
    public async Task Provisioning_ExistingUserWithPendingInvite_AutoAccepts()
    {
        var (workspace, _) = await SeedWorkspaceWithOwnerAsync();
        var invitee = await SeedUserAsync("invitee", "invitee@example.com");
        _db.WorkspaceMembers.Add(new WorkspaceMember
        {
            WorkspaceId = workspace.Id,
            UserId = null,
            Role = WorkspaceRole.Viewer,
            InviteEmail = "invitee@example.com",
            InvitedAtUtc = DateTime.UtcNow,
            JoinedAtUtc = null
        });
        await _db.SaveChangesAsync();

        await RunProvisioningAsync("invitee", "invitee@example.com");

        using var read = CreateReadContext();
        var member = await read.WorkspaceMembers.SingleAsync(m =>
            m.WorkspaceId == workspace.Id && m.UserId == invitee.Id);
        Assert.Equal(WorkspaceRole.Viewer, member.Role);
        Assert.NotNull(member.JoinedAtUtc);
    }

    // -------------------------------------------------------------------------
    // SetRoleByMemberIdAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SetRole_OwnerCanPromoteEditorToOwner()
    {
        var (workspace, owner) = await SeedWorkspaceWithOwnerAsync();
        var (_, editor) = await AddMemberAsync(workspace.Id, "editor", WorkspaceRole.Editor);
        var sut = BuildService();

        var updated = await sut.SetRoleByMemberIdAsync(editor.Id, WorkspaceRole.Owner, owner.Id);

        Assert.True(updated);
        Assert.Equal(WorkspaceRole.Owner, (await _db.WorkspaceMembers.FindAsync(editor.Id))!.Role);
    }

    [Fact]
    public async Task SetRole_LastOwnerCannotBeDemoted()
    {
        var (workspace, owner) = await SeedWorkspaceWithOwnerAsync();
        var ownerMember = await _db.WorkspaceMembers.SingleAsync(m =>
            m.WorkspaceId == workspace.Id && m.Role == WorkspaceRole.Owner);
        var sut = BuildService();

        var updated = await sut.SetRoleByMemberIdAsync(ownerMember.Id, WorkspaceRole.Editor, owner.Id);

        Assert.False(updated);
        Assert.Equal(WorkspaceRole.Owner, (await _db.WorkspaceMembers.FindAsync(ownerMember.Id))!.Role);
    }

    [Fact]
    public async Task SetRole_NonOwner_ReturnsFalse()
    {
        var (workspace, owner) = await SeedWorkspaceWithOwnerAsync();
        var (_, editor) = await AddMemberAsync(workspace.Id, "editor", WorkspaceRole.Editor);
        var other = await SeedUserAsync("other", "other@example.com");
        await AddMemberAsync(workspace.Id, "other", WorkspaceRole.Viewer, other);
        var sut = BuildService();

        var updated = await sut.SetRoleByMemberIdAsync(editor.Id, WorkspaceRole.Owner, other.Id);

        Assert.False(updated);
        Assert.Equal(WorkspaceRole.Editor, (await _db.WorkspaceMembers.FindAsync(editor.Id))!.Role);
    }

    // -------------------------------------------------------------------------
    // RemoveMemberByIdAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RemoveMember_OwnerCanRemoveEditor()
    {
        var (workspace, owner) = await SeedWorkspaceWithOwnerAsync();
        var (_, editor) = await AddMemberAsync(workspace.Id, "editor", WorkspaceRole.Editor);
        var sut = BuildService();

        var removed = await sut.RemoveMemberByIdAsync(editor.Id, owner.Id);

        Assert.True(removed);
        Assert.Null(await _db.WorkspaceMembers.FindAsync(editor.Id));
    }

    [Fact]
    public async Task RemoveMember_LastOwnerCannotRemoveSelf()
    {
        var (workspace, owner) = await SeedWorkspaceWithOwnerAsync();
        var ownerMember = await _db.WorkspaceMembers.SingleAsync(m =>
            m.WorkspaceId == workspace.Id && m.Role == WorkspaceRole.Owner);
        var sut = BuildService();

        var removed = await sut.RemoveMemberByIdAsync(ownerMember.Id, owner.Id);

        Assert.False(removed);
        Assert.NotNull(await _db.WorkspaceMembers.FindAsync(ownerMember.Id));
    }

    [Fact]
    public async Task RemoveMember_EditorCannotRemoveOthers()
    {
        var (workspace, _) = await SeedWorkspaceWithOwnerAsync();
        var (_, editor) = await AddMemberAsync(workspace.Id, "editor", WorkspaceRole.Editor);
        var (_, viewer) = await AddMemberAsync(workspace.Id, "viewer", WorkspaceRole.Viewer);
        var sut = BuildService();

        var removed = await sut.RemoveMemberByIdAsync(viewer.Id, editor.Id);

        Assert.False(removed);
        Assert.NotNull(await _db.WorkspaceMembers.FindAsync(viewer.Id));
    }

    // -------------------------------------------------------------------------
    // DeleteWorkspaceAsync + cascade semantics
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DeleteWorkspace_OwnerWithNoLinks_DeletesWorkspaceAndMembers()
    {
        var (workspace, owner) = await SeedWorkspaceWithOwnerAsync();
        await AddMemberAsync(workspace.Id, "editor", WorkspaceRole.Editor);
        var sut = BuildService();

        var deleted = await sut.DeleteWorkspaceAsync(workspace.Id, owner.Id);

        Assert.True(deleted);
        Assert.Null(await _db.Workspaces.FindAsync(workspace.Id));
        Assert.Empty(await _db.WorkspaceMembers.Where(m => m.WorkspaceId == workspace.Id).ToListAsync());
    }

    [Fact]
    public async Task DeleteWorkspace_WhenLinksExist_ReturnsFalse()
    {
        var (workspace, owner) = await SeedWorkspaceWithOwnerAsync();
        _db.ShortenedUrls.Add(new ShortenedUrl
        {
            LongUrl = "https://example.com/x",
            ShortCode = "abc123",
            WorkspaceId = workspace.Id,
            CreatedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        var sut = BuildService();

        var deleted = await sut.DeleteWorkspaceAsync(workspace.Id, owner.Id);

        Assert.False(deleted);
        Assert.NotNull(await _db.Workspaces.FindAsync(workspace.Id));
    }

    [Fact]
    public async Task DeleteWorkspace_NonOwner_ReturnsFalse()
    {
        var (workspace, _) = await SeedWorkspaceWithOwnerAsync();
        var (_, editor) = await AddMemberAsync(workspace.Id, "editor", WorkspaceRole.Editor);
        var sut = BuildService();

        var deleted = await sut.DeleteWorkspaceAsync(workspace.Id, editor.Id);

        Assert.False(deleted);
        Assert.NotNull(await _db.Workspaces.FindAsync(workspace.Id));
    }

    [Fact]
    public async Task RemovingWorkspaceRow_UnlinksShortenedUrlsAndDomains()
    {
        // The service refuses to delete a workspace that still has links, so the
        // SetNull cascade on ShortenedUrl.WorkspaceId / Domain.WorkspaceId only
        // runs when the workspace row is removed directly. Pin that FK behaviour.
        var (workspace, _) = await SeedWorkspaceWithOwnerAsync();
        var link = new ShortenedUrl
        {
            LongUrl = "https://example.com/x",
            ShortCode = "abc123",
            WorkspaceId = workspace.Id,
            CreatedAtUtc = DateTime.UtcNow
        };
        var domain = new Domain
        {
            Hostname = "go.example.com",
            WorkspaceId = workspace.Id,
            IsVerified = false,
            VerificationToken = "tok",
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.ShortenedUrls.Add(link);
        _db.Domains.Add(domain);
        await _db.SaveChangesAsync();

        _db.Workspaces.Remove(await _db.Workspaces.FindAsync(workspace.Id));
        await _db.SaveChangesAsync();

        Assert.Null((await _db.ShortenedUrls.FindAsync(link.Id))!.WorkspaceId);
        Assert.Null((await _db.Domains.FindAsync(domain.Id))!.WorkspaceId);
        Assert.Empty(await _db.WorkspaceMembers.Where(m => m.WorkspaceId == workspace.Id).ToListAsync());
    }

    // -------------------------------------------------------------------------
    // Membership queries
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetRoleAsync_PendingMember_HasNoRole()
    {
        var (workspace, _) = await SeedWorkspaceWithOwnerAsync();
        _db.WorkspaceMembers.Add(new WorkspaceMember
        {
            WorkspaceId = workspace.Id,
            UserId = null,
            Role = WorkspaceRole.Editor,
            InviteEmail = "pending@example.com",
            InvitedAtUtc = DateTime.UtcNow,
            JoinedAtUtc = null
        });
        await _db.SaveChangesAsync();
        var stranger = await SeedUserAsync("stranger", "stranger@example.com");
        var sut = BuildService();

        var role = await sut.GetRoleAsync(workspace.Id, stranger.Id);

        Assert.Null(role);
    }

    [Fact]
    public async Task GetWorkspacesForUserAsync_OnlyIncludesJoinedWorkspaces()
    {
        var owner = await SeedUserAsync("owner", "owner@example.com");
        var workspaceA = await BuildService().CreateAsync("Team A", "team-a", owner.Id);
        // A workspace owned by someone else invites the owner but they never join.
        var ownerB = await SeedUserAsync("owner-b", "owner-b@example.com");
        var workspaceB = await BuildService().CreateAsync("Team B", "team-b", ownerB.Id);
        _db.WorkspaceMembers.Add(new WorkspaceMember
        {
            WorkspaceId = workspaceB.Id,
            UserId = owner.Id,
            Role = WorkspaceRole.Editor,
            InviteEmail = owner.Email,
            InvitedAtUtc = DateTime.UtcNow,
            JoinedAtUtc = null
        });
        await _db.SaveChangesAsync();
        var sut = BuildService();

        var workspaces = await sut.GetWorkspacesForUserAsync(owner.Id);

        Assert.Contains(workspaces, w => w.Id == workspaceA.Id);
        Assert.DoesNotContain(workspaces, w => w.Id == workspaceB.Id);
    }

    // -------------------------------------------------------------------------
    // ResendInviteAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ResendInvite_Owner_UpdatesInvitedAt()
    {
        var (workspace, owner) = await SeedWorkspaceWithOwnerAsync();
        await _db.WorkspaceMembers.AddAsync(new WorkspaceMember
        {
            WorkspaceId = workspace.Id,
            UserId = null,
            Role = WorkspaceRole.Editor,
            InviteEmail = "pending@example.com",
            InvitedAtUtc = DateTime.UtcNow.AddDays(-5),
            JoinedAtUtc = null
        });
        await _db.SaveChangesAsync();
        var sut = BuildService();
        var pending = await _db.WorkspaceMembers.SingleAsync(m => m.InviteEmail == "pending@example.com");

        var resent = await sut.ResendInviteAsync(pending.Id, owner.Id);

        Assert.True(resent);
        var refreshed = await _db.WorkspaceMembers.AsNoTracking().SingleAsync(m => m.Id == pending.Id);
        Assert.True(refreshed.InvitedAtUtc > DateTime.UtcNow.AddHours(-1));
    }

    [Fact]
    public async Task ResendInvite_NonOwner_ReturnsFalse()
    {
        var (workspace, _) = await SeedWorkspaceWithOwnerAsync();
        var (_, editor) = await AddMemberAsync(workspace.Id, "editor", WorkspaceRole.Editor);
        _db.WorkspaceMembers.Add(new WorkspaceMember
        {
            WorkspaceId = workspace.Id,
            UserId = null,
            Role = WorkspaceRole.Editor,
            InviteEmail = "pending@example.com",
            InvitedAtUtc = DateTime.UtcNow,
            JoinedAtUtc = null
        });
        await _db.SaveChangesAsync();
        var sut = BuildService();
        var pending = await _db.WorkspaceMembers.SingleAsync(m => m.InviteEmail == "pending@example.com");

        var resent = await sut.ResendInviteAsync(pending.Id, editor.Id);

        Assert.False(resent);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private WorkspaceService BuildService() =>
        new(_db, BuildEmailService(), new HttpContextAccessor());

    private AppDbContext CreateReadContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);

    private static EmailService BuildEmailService() =>
        new(Options.Create(new SmtpOptions { Host = "127.0.0.1", Port = 1 }),
            NullLogger<EmailService>.Instance);

    private async Task<User> SeedUserAsync(string subject, string? email)
    {
        var user = new User
        {
            Issuer = TestIssuer,
            Subject = subject,
            Email = email,
            CreatedAtUtc = DateTime.UtcNow,
            LastLoginAtUtc = DateTime.UtcNow
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    private async Task<(Workspace Workspace, User Owner)> SeedWorkspaceWithOwnerAsync()
    {
        var owner = await SeedUserAsync("owner", "owner@example.com");
        var workspace = await BuildService().CreateAsync("Acme", "acme", owner.Id);
        return (workspace, owner);
    }

    private async Task<(WorkspaceMember Member, User User)> AddMemberAsync(long workspaceId, string subject, WorkspaceRole role)
    {
        var user = await SeedUserAsync(subject, $"{subject}@example.com");
        return (await AddMemberAsync(workspaceId, subject, role, user), user);
    }

    private async Task<WorkspaceMember> AddMemberAsync(long workspaceId, string subject, WorkspaceRole role, User user)
    {
        var member = new WorkspaceMember
        {
            WorkspaceId = workspaceId,
            UserId = user.Id,
            Role = role,
            InviteEmail = $"{subject}@example.com",
            InvitedAtUtc = DateTime.UtcNow,
            JoinedAtUtc = DateTime.UtcNow
        };
        _db.WorkspaceMembers.Add(member);
        await _db.SaveChangesAsync();
        return member;
    }

    private async Task RunProvisioningAsync(string subject, string email)
    {
        var channel = Channel.CreateUnbounded<PendingUserLogin>();
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(_connection));
        services.AddLogging();
        await using var provider = services.BuildServiceProvider();

        var processor = new ExposedUserProvisioningProcessor(
            channel,
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<UserProvisioningProcessor>.Instance);

        channel.Writer.TryWrite(new PendingUserLogin
        {
            Issuer = TestIssuer,
            Subject = subject,
            Email = email,
            Name = subject
        });
        channel.Writer.Complete();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await processor.RunAsync(cts.Token);
    }

    private sealed class ExposedUserProvisioningProcessor : UserProvisioningProcessor
    {
        public ExposedUserProvisioningProcessor(
            Channel<PendingUserLogin> channel,
            IServiceScopeFactory scopeFactory,
            ILogger<UserProvisioningProcessor> logger)
            : base(channel, scopeFactory, logger)
        {
        }

        public Task RunAsync(CancellationToken ct) => ExecuteAsync(ct);
    }
}
