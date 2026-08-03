using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;

namespace Shortnr.Web.Services;

public partial class WorkspaceService(AppDbContext db)
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
            .Include(m => m.Workspace)
            .Select(m => m.Workspace!)
            .OrderBy(w => w.Name)
            .ToListAsync();

    public async Task<Workspace?> GetWorkspaceBySlugAsync(string slug) =>
        await db.Workspaces.FirstOrDefaultAsync(w => w.Slug == slug);

    public async Task<WorkspaceMember?> GetMemberAsync(long workspaceId, long userId) =>
        await db.WorkspaceMembers
            .FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == userId && m.JoinedAtUtc != null);

    public async Task<WorkspaceRole?> GetRoleAsync(long workspaceId, long userId)
    {
        var member = await GetMemberAsync(workspaceId, userId);
        return member?.Role;
    }

    public async Task<bool> IsMemberAsync(long workspaceId, long userId) =>
        await GetMemberAsync(workspaceId, userId) is not null;

    public async Task<bool> InviteMemberAsync(long workspaceId, string email, WorkspaceRole role, long actorUserId)
    {
        var actorMember = await GetMemberAsync(workspaceId, actorUserId);
        if (actorMember is null || actorMember.Role != WorkspaceRole.Owner)
            return false;

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var existingUser = await db.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail);
        if (existingUser is not null)
        {
            var already = await db.WorkspaceMembers
                .AnyAsync(m => m.WorkspaceId == workspaceId && m.UserId == existingUser.Id && m.JoinedAtUtc != null);
            if (already)
                return false;

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
                UserId = 0,
                Role = role,
                InviteEmail = normalizedEmail,
                InvitedAtUtc = DateTime.UtcNow,
                JoinedAtUtc = null
            });
        }

        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> SetRoleAsync(long workspaceId, long targetUserId, WorkspaceRole newRole, long actorUserId)
    {
        var actorMember = await GetMemberAsync(workspaceId, actorUserId);
        if (actorMember is null || actorMember.Role != WorkspaceRole.Owner)
            return false;

        var target = await db.WorkspaceMembers
            .FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == targetUserId && m.JoinedAtUtc != null);
        if (target is null)
            return false;

        if (target.Role == WorkspaceRole.Owner && newRole != WorkspaceRole.Owner)
        {
            var ownerCount = await db.WorkspaceMembers
                .CountAsync(m => m.WorkspaceId == workspaceId && m.Role == WorkspaceRole.Owner && m.JoinedAtUtc != null);
            if (ownerCount <= 1)
                return false;
        }

        target.Role = newRole;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RemoveMemberAsync(long workspaceId, long targetUserId, long actorUserId)
    {
        var actorMember = await GetMemberAsync(workspaceId, actorUserId);
        if (actorMember is null)
            return false;

        if (actorUserId != targetUserId && actorMember.Role != WorkspaceRole.Owner)
            return false;

        var target = await db.WorkspaceMembers
            .FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == targetUserId);
        if (target is null)
            return false;

        if (target.Role == WorkspaceRole.Owner)
        {
            var ownerCount = await db.WorkspaceMembers
                .CountAsync(m => m.WorkspaceId == workspaceId && m.Role == WorkspaceRole.Owner && m.JoinedAtUtc != null);
            if (ownerCount <= 1)
                return false;
        }

        db.WorkspaceMembers.Remove(target);
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
