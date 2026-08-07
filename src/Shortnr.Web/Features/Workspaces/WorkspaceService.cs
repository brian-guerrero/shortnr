using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;

namespace Shortnr.Web.Features.Workspaces;

public partial class WorkspaceService(AppDbContext db, EmailService emailService, IHttpContextAccessor httpContextAccessor)
{
    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9_-]{2,31}$")]
    private static partial Regex SlugPattern();

    public static bool IsValidSlug(string slug) => SlugPattern().IsMatch(slug);

    public static string GenerateSlug() => ShortLinkCodes.GenerateCode();

    public async Task<Workspace> CreateAsync(string name, string slug, long ownerUserId)
    {
        var workspace = new Workspace
        {
            Name = name.Trim(),
            Slug = slug.Trim(),
            OwnerUserId = ownerUserId,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.Workspaces.Add(workspace);

        db.WorkspaceMembers.Add(new WorkspaceMember
        {
            Workspace = workspace,
            UserId = ownerUserId,
            Role = WorkspaceRole.Owner,
            InvitedAtUtc = DateTime.UtcNow,
            JoinedAtUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
        return workspace;
    }

    public async Task<List<Workspace>> GetWorkspacesForUserAsync(long userId) =>
        await db.WorkspaceMembers
            .Where(m => m.UserId == userId && m.JoinedAtUtc != null)
            .AsNoTracking()
            .Select(m => m.Workspace!)
            .OrderBy(w => w.Name)
            .ToListAsync();

    public async Task<Workspace?> GetWorkspaceBySlugAsync(string slug) =>
        await db.Workspaces.FirstOrDefaultAsync(w => w.Slug == slug);

    public async Task<WorkspaceMember?> GetMemberAsync(long workspaceId, long userId) =>
        await db.WorkspaceMembers
            .FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == userId && m.JoinedAtUtc != null);

    public async Task<WorkspaceMember?> GetMemberByIdAsync(long memberId) =>
        await db.WorkspaceMembers.FindAsync(memberId);

    public async Task<WorkspaceRole?> GetRoleAsync(long workspaceId, long userId) =>
        await db.WorkspaceMembers
            .Where(m => m.WorkspaceId == workspaceId && m.UserId == userId && m.JoinedAtUtc != null)
            .Select(m => (WorkspaceRole?)m.Role)
            .FirstOrDefaultAsync();

    public async Task<bool> IsMemberAsync(long workspaceId, long userId) =>
        await db.WorkspaceMembers
            .AnyAsync(m => m.WorkspaceId == workspaceId && m.UserId == userId && m.JoinedAtUtc != null);

    public async Task<bool> InviteMemberAsync(long workspaceId, string email, WorkspaceRole role, long actorUserId)
    {
        var actorMember = await GetMemberAsync(workspaceId, actorUserId);
        if (actorMember is null || actorMember.Role != WorkspaceRole.Owner)
            return false;

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var workspace = await db.Workspaces.FindAsync(workspaceId);
        if (workspace is null)
            return false;

        var existingUser = await db.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail);
        if (existingUser is not null)
        {
            var alreadyActive = await db.WorkspaceMembers
                .AnyAsync(m => m.WorkspaceId == workspaceId && m.UserId == existingUser.Id && m.JoinedAtUtc != null);
            if (alreadyActive)
                return false;
        }
        else
        {
            var alreadyPending = await db.WorkspaceMembers
                .AnyAsync(m => m.WorkspaceId == workspaceId && m.InviteEmail == normalizedEmail && m.JoinedAtUtc == null);
            if (alreadyPending)
                return false;
        }

        if (existingUser is not null)
        {
            db.WorkspaceMembers.Add(new WorkspaceMember
            {
                WorkspaceId = workspaceId,
                UserId = existingUser.Id,
                Role = role,
                InviteEmail = normalizedEmail,
                InvitedAtUtc = DateTime.UtcNow,
                JoinedAtUtc = DateTime.UtcNow
            });
        }
        else
        {
            db.WorkspaceMembers.Add(new WorkspaceMember
            {
                WorkspaceId = workspaceId,
                UserId = null,
                Role = role,
                InviteEmail = normalizedEmail,
                InvitedAtUtc = DateTime.UtcNow,
                JoinedAtUtc = null
            });
        }

        await db.SaveChangesAsync();

        var host = httpContextAccessor.HttpContext?.Request.Host.Host ?? "localhost";
        var scheme = httpContextAccessor.HttpContext?.Request.Scheme ?? "http";
        var actorUser = await db.Users.FindAsync(actorUserId);

        _ = emailService.SendAsync(
            normalizedEmail,
            $"You've been invited to join '{workspace.Name}' on shortnr",
            $"Hi,\n\n{actorUser?.Name ?? actorUser?.Email ?? "Someone"} has invited you to join the workspace '{workspace.Name}' on shortnr.\n\nYour role: {role}\n\n{scheme}://{host}/account/login\n\nLog in with your account to accept. If you don't have an account yet, sign up and you'll be added automatically.\n");

        return true;
    }

    public async Task<bool> SetRoleByMemberIdAsync(long memberId, WorkspaceRole newRole, long actorUserId)
    {
        var target = await db.WorkspaceMembers.FindAsync(memberId);
        if (target is null)
            return false;

        var actorMember = await GetMemberAsync(target.WorkspaceId, actorUserId);
        if (actorMember is null || actorMember.Role != WorkspaceRole.Owner)
            return false;

        if (target.Role == WorkspaceRole.Owner && newRole != WorkspaceRole.Owner)
        {
            var ownerCount = await db.WorkspaceMembers
                .CountAsync(m => m.WorkspaceId == target.WorkspaceId && m.Role == WorkspaceRole.Owner && m.JoinedAtUtc != null);
            if (ownerCount <= 1)
                return false;
        }

        target.Role = newRole;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RemoveMemberByIdAsync(long memberId, long actorUserId)
    {
        var target = await db.WorkspaceMembers.FindAsync(memberId);
        if (target is null)
            return false;

        var actorMember = await GetMemberAsync(target.WorkspaceId, actorUserId);
        if (actorMember is null)
            return false;

        if (actorUserId != target.UserId && actorMember.Role != WorkspaceRole.Owner)
            return false;

        if (target.Role == WorkspaceRole.Owner)
        {
            var ownerCount = await db.WorkspaceMembers
                .CountAsync(m => m.WorkspaceId == target.WorkspaceId && m.Role == WorkspaceRole.Owner && m.JoinedAtUtc != null);
            if (ownerCount <= 1)
                return false;
        }

        db.WorkspaceMembers.Remove(target);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ResendInviteAsync(long memberId, long actorUserId)
    {
        var target = await db.WorkspaceMembers.FindAsync(memberId);
        if (target is null || target.JoinedAtUtc is not null || target.InviteEmail is null)
            return false;

        var actorMember = await GetMemberAsync(target.WorkspaceId, actorUserId);
        if (actorMember is null || actorMember.Role != WorkspaceRole.Owner)
            return false;

        var workspace = await db.Workspaces.FindAsync(target.WorkspaceId);
        if (workspace is null)
            return false;

        var host = httpContextAccessor.HttpContext?.Request.Host.Host ?? "localhost";
        var scheme = httpContextAccessor.HttpContext?.Request.Scheme ?? "http";
        var actorUser = await db.Users.FindAsync(actorUserId);

        _ = emailService.SendAsync(
            target.InviteEmail,
            $"You've been invited to join '{workspace.Name}' on shortnr",
            $"Hi,\n\n{actorUser?.Name ?? actorUser?.Email ?? "Someone"} has invited you to join the workspace '{workspace.Name}' on shortnr.\n\nYour role: {target.Role}\n\n{scheme}://{host}/account/login\n\nLog in with your account to accept. If you don't have an account yet, sign up and you'll be added automatically.\n");

        target.InvitedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteWorkspaceAsync(long workspaceId, long actorUserId)
    {
        var actorMember = await GetMemberAsync(workspaceId, actorUserId);
        if (actorMember is null || actorMember.Role != WorkspaceRole.Owner)
            return false;

        var hasLinks = await db.ShortenedUrls.AnyAsync(l => l.WorkspaceId == workspaceId);
        if (hasLinks)
            return false;

        var workspace = await db.Workspaces.FindAsync(workspaceId);
        if (workspace is null)
            return false;

        db.Workspaces.Remove(workspace);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<List<WorkspaceMember>> GetMembersAsync(long workspaceId) =>
        await db.WorkspaceMembers
            .Where(m => m.WorkspaceId == workspaceId)
            .Include(m => m.User)
            .OrderBy(m => m.Role)
            .ThenBy(m => m.InvitedAtUtc)
            .ToListAsync();
}
