using Shortnr.Data;
using Shortnr.Data.Entities;

namespace Shortnr.Web.Services;

public class WorkspaceAuthorizationService(WorkspaceService workspaceService)
{
    public async Task<bool> CanCreateLinkAsync(long? workspaceId, long? userId)
    {
        if (workspaceId is null || userId is null)
            return false;
        var role = await workspaceService.GetRoleAsync(workspaceId.Value, userId.Value);
        return role is WorkspaceRole.Owner or WorkspaceRole.Editor;
    }

    public async Task<bool> CanEditLinkAsync(long? workspaceId, long? userId)
    {
        if (workspaceId is null || userId is null)
            return false;
        var role = await workspaceService.GetRoleAsync(workspaceId.Value, userId.Value);
        return role is WorkspaceRole.Owner or WorkspaceRole.Editor;
    }

    public async Task<bool> CanDeleteLinkAsync(long? workspaceId, long? userId)
    {
        if (workspaceId is null || userId is null)
            return false;
        var role = await workspaceService.GetRoleAsync(workspaceId.Value, userId.Value);
        return role is WorkspaceRole.Owner or WorkspaceRole.Editor;
    }

    public async Task<bool> CanViewLinksAsync(long workspaceId, long userId) =>
        await workspaceService.IsMemberAsync(workspaceId, userId);

    public async Task<bool> CanManageMembersAsync(long workspaceId, long userId)
    {
        var role = await workspaceService.GetRoleAsync(workspaceId, userId);
        return role == WorkspaceRole.Owner;
    }

    public async Task<bool> CanDeleteWorkspaceAsync(long workspaceId, long userId)
    {
        var role = await workspaceService.GetRoleAsync(workspaceId, userId);
        return role == WorkspaceRole.Owner;
    }
}
