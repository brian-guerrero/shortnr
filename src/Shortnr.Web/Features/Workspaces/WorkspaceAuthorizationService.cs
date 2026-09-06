using Shortnr.Data;
using Shortnr.Data.Entities;

namespace Shortnr.Web.Features.Workspaces;

public class WorkspaceAuthorizationService(WorkspaceService workspaceService)
{
    public Task<bool> CanCreateLinkAsync(long? workspaceId, long? userId) =>
        HasWriteRoleAsync(workspaceId, userId);

    public Task<bool> CanEditLinkAsync(long? workspaceId, long? userId) =>
        HasWriteRoleAsync(workspaceId, userId);

    public Task<bool> CanDeleteLinkAsync(long? workspaceId, long? userId) =>
        HasWriteRoleAsync(workspaceId, userId);

    public async Task<bool> CanViewLinksAsync(long workspaceId, long userId) =>
        await workspaceService.IsMemberAsync(workspaceId, userId);

    public Task<bool> CanManageMembersAsync(long workspaceId, long userId) =>
        IsOwnerAsync(workspaceId, userId);

    public Task<bool> CanDeleteWorkspaceAsync(long workspaceId, long userId) =>
        IsOwnerAsync(workspaceId, userId);

    /// <summary>Owners and editors may write links; viewers may not.</summary>
    private async Task<bool> HasWriteRoleAsync(long? workspaceId, long? userId)
    {
        if (workspaceId is null || userId is null)
            return false;
        var role = await workspaceService.GetRoleAsync(workspaceId.Value, userId.Value);
        return role is WorkspaceRole.Owner or WorkspaceRole.Editor;
    }

    private async Task<bool> IsOwnerAsync(long workspaceId, long userId)
    {
        var role = await workspaceService.GetRoleAsync(workspaceId, userId);
        return role == WorkspaceRole.Owner;
    }
}
