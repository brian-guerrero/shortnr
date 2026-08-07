using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Tests.Integration.Infrastructure;

namespace Shortnr.Tests.Integration.Workspaces;

/// <summary>
/// Verifies the /settings/workspaces page end-to-end: creating a workspace,
/// inviting members, changing roles, deleting, and auth gating.
/// </summary>
public class WorkspacesSettingsTests : IAsyncLifetime
{
    private static readonly Regex AntiforgeryTokenRegex = new(
        @"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""",
        RegexOptions.Compiled);

    private readonly ShortnrWebAppFactory _factory = new(authEnabled: true);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task CreateWorkspace_AddsWorkspaceAndOwnerMembership()
    {
        await SeedAuthenticatedUserAsync("alice");
        var client = _factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/settings/workspaces", token,
            ("name", "Acme"), ("slug", "acme"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Acme", body);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var workspace = await db.Workspaces.SingleAsync(w => w.Slug == "acme");
        var member = await db.WorkspaceMembers.SingleAsync(m => m.WorkspaceId == workspace.Id);
        Assert.Equal(WorkspaceRole.Owner, member.Role);
        Assert.NotNull(member.JoinedAtUtc);
    }

    [Fact]
    public async Task CreateWorkspace_InvalidSlug_ReturnsError()
    {
        await SeedAuthenticatedUserAsync("alice");
        var client = _factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/settings/workspaces", token,
            ("name", "Acme"), ("slug", "!!invalid!!"));

        Assert.Contains("Slug must", await response.Content.ReadAsStringAsync());
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await db.Workspaces.CountAsync());
    }

    [Fact]
    public async Task InviteMember_AddsPendingMember()
    {
        var alice = await SeedAuthenticatedUserAsync("alice");
        var workspace = await SeedWorkspaceAsync(alice.Id, "acme");
        var client = _factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, $"/settings/workspaces?handler=Invite&id={workspace.Id}", token,
            ("email", "newbie@example.com"), ("role", "1"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Invitation sent", await response.Content.ReadAsStringAsync());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var member = await db.WorkspaceMembers.SingleAsync(m =>
            m.WorkspaceId == workspace.Id && m.InviteEmail == "newbie@example.com");
        Assert.Null(member.UserId);
        Assert.Null(member.JoinedAtUtc);
        Assert.Equal(WorkspaceRole.Editor, member.Role);
    }

    [Fact]
    public async Task SetRole_OwnerCanChangeMemberRole()
    {
        var alice = await SeedAuthenticatedUserAsync("alice");
        var workspace = await SeedWorkspaceAsync(alice.Id, "acme");
        var editor = await SeedMemberAsync(workspace.Id, "editor", WorkspaceRole.Editor);
        var client = _factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, $"/settings/workspaces?handler=SetRole&id={workspace.Id}", token,
            ("memberId", editor.Id.ToString()), ("role", "0"));

        Assert.Contains("Role updated", await response.Content.ReadAsStringAsync());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(WorkspaceRole.Viewer, (await db.WorkspaceMembers.FindAsync(editor.Id))!.Role);
    }

    [Fact]
    public async Task DeleteWorkspace_OwnerWithNoLinks_DeletesIt()
    {
        var alice = await SeedAuthenticatedUserAsync("alice");
        var workspace = await SeedWorkspaceAsync(alice.Id, "acme");
        var client = _factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, $"/settings/workspaces?handler=Delete&id={workspace.Id}", token);

        Assert.Contains("Workspace deleted", await response.Content.ReadAsStringAsync());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await db.Workspaces.CountAsync(w => w.Id == workspace.Id));
        Assert.Equal(0, await db.WorkspaceMembers.CountAsync(m => m.WorkspaceId == workspace.Id));
    }

    [Fact]
    public async Task WhenAuthEnabled_UnauthenticatedFullPage_RedirectsToIndex()
    {
        var client = _factory.CreateClientNoRedirect();

        var response = await client.GetAsync("/settings/workspaces");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task WhenAuthEnabled_UnauthenticatedHtmx_ReturnsUnauthorized()
    {
        var client = _factory.CreateClientNoRedirect();
        var request = new HttpRequestMessage(HttpMethod.Get, "/settings/workspaces");
        request.Headers.Add("HX-Request", "true");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<User> SeedAuthenticatedUserAsync(string subject)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Users.RemoveRange(db.Users);
        db.WorkspaceMembers.RemoveRange(db.WorkspaceMembers);
        db.Workspaces.RemoveRange(db.Workspaces);
        await db.SaveChangesAsync();

        var user = new User
        {
            Issuer = ShortnrWebAppFactory.TestIssuer,
            Subject = subject,
            Email = $"{subject}@example.com",
            CreatedAtUtc = DateTime.UtcNow,
            LastLoginAtUtc = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        _factory.Services.GetRequiredService<TestAuthState>().SetAuthenticatedUser(subject, ShortnrWebAppFactory.TestIssuer);
        return user;
    }

    private async Task<Workspace> SeedWorkspaceAsync(long ownerUserId, string slug)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var workspace = new Workspace
        {
            Name = slug,
            Slug = slug,
            OwnerUserId = ownerUserId,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.Workspaces.Add(workspace);
        await db.SaveChangesAsync();

        db.WorkspaceMembers.Add(new WorkspaceMember
        {
            WorkspaceId = workspace.Id,
            UserId = ownerUserId,
            Role = WorkspaceRole.Owner,
            InvitedAtUtc = DateTime.UtcNow,
            JoinedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return workspace;
    }

    private async Task<WorkspaceMember> SeedMemberAsync(long workspaceId, string subject, WorkspaceRole role)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new User
        {
            Issuer = ShortnrWebAppFactory.TestIssuer,
            Subject = subject,
            Email = $"{subject}@example.com",
            CreatedAtUtc = DateTime.UtcNow,
            LastLoginAtUtc = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var member = new WorkspaceMember
        {
            WorkspaceId = workspaceId,
            UserId = user.Id,
            Role = role,
            InvitedAtUtc = DateTime.UtcNow,
            JoinedAtUtc = DateTime.UtcNow
        };
        db.WorkspaceMembers.Add(member);
        await db.SaveChangesAsync();
        return member;
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        var page = await client.GetAsync("/settings/workspaces");
        var html = await page.Content.ReadAsStringAsync();
        var match = AntiforgeryTokenRegex.Match(html);
        Assert.True(match.Success, "Antiforgery token not found in workspaces settings page.");
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
