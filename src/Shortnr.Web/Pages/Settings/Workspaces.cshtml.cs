using Microsoft.AspNetCore.Mvc;
using Shortnr.Data.Entities;

namespace Shortnr.Web.Pages.Settings;

public class WorkspacesModel(WorkspaceService workspaceService, UserIdentityService identity)
    : AuthorizedPageModel(identity)
{
    public List<Workspace> Workspaces { get; set; } = [];
    public string? ExpandedWorkspace { get; set; }

    public async Task<IActionResult> OnGet()
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        Workspaces = await LoadWorkspacesAsync();
        return Page();
    }

    public async Task<IActionResult> OnPost(string name, string? slug)
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        var ownerUserId = await identity.ResolveOwnerUserIdAsync(User);
        if (ownerUserId is null)
            return await ListPartialAsync(error: "Unable to determine your account.");

        name = name.Trim();
        if (name.Length == 0)
            return await ListPartialAsync(error: "Enter a workspace name.");

        slug = (slug ?? "").Trim();
        if (slug.Length == 0)
            slug = WorkspaceService.GenerateSlug();

        if (!WorkspaceService.IsValidSlug(slug))
            return await ListPartialAsync(error: "Slug must be 3–32 characters: letters, digits, '-' and '_', starting with a letter or digit.");

        var workspace = await workspaceService.CreateAsync(name, slug, ownerUserId.Value);
        return await ListPartialAsync(status: $"Workspace '{workspace.Name}' created.");
    }

    public async Task<IActionResult> OnPostDelete(long id)
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        var ownerUserId = await identity.ResolveOwnerUserIdAsync(User);
        if (ownerUserId is null)
            return await ListPartialAsync(error: "Unable to determine your account.");

        var deleted = await workspaceService.DeleteWorkspaceAsync(id, ownerUserId.Value);
        if (!deleted)
            return await ListPartialAsync(error: "Cannot delete this workspace. Make sure no links exist in it and you are the owner.");

        return await ListPartialAsync(status: "Workspace deleted.");
    }

    public async Task<IActionResult> OnPostInvite(long id, string email, WorkspaceRole role)
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        var ownerUserId = await identity.ResolveOwnerUserIdAsync(User);
        if (ownerUserId is null)
            return await DetailPartialAsync(id, error: "Unable to determine your account.");

        email = email.Trim();
        if (email.Length == 0)
            return await DetailPartialAsync(id, error: "Enter an email address.");

        var invited = await workspaceService.InviteMemberAsync(id, email, role, ownerUserId.Value);
        if (!invited)
            return await DetailPartialAsync(id, error: "Invite failed. Make sure the email isn't already a member and you are the owner.");

        return await DetailPartialAsync(id, status: $"Invitation sent to {email}.");
    }

    public async Task<IActionResult> OnPostSetRole(long id, long memberId, WorkspaceRole role)
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        var ownerUserId = await identity.ResolveOwnerUserIdAsync(User);
        if (ownerUserId is null)
            return await DetailPartialAsync(id, error: "Unable to determine your account.");

        var updated = await workspaceService.SetRoleByMemberIdAsync(memberId, role, ownerUserId.Value);
        if (!updated)
            return await DetailPartialAsync(id, error: "Cannot change this member's role.");

        return await DetailPartialAsync(id, status: "Role updated.");
    }

    public async Task<IActionResult> OnPostRemoveMember(long id, long memberId)
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        var ownerUserId = await identity.ResolveOwnerUserIdAsync(User);
        if (ownerUserId is null)
            return await DetailPartialAsync(id, error: "Unable to determine your account.");

        var removed = await workspaceService.RemoveMemberByIdAsync(memberId, ownerUserId.Value);
        if (!removed)
            return await DetailPartialAsync(id, error: "Cannot remove this member. A workspace must keep at least one owner.");

        return await DetailPartialAsync(id, status: "Member removed.");
    }

    public async Task<IActionResult> OnPostResendInvite(long id, long memberId)
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        var ownerUserId = await identity.ResolveOwnerUserIdAsync(User);
        if (ownerUserId is null)
            return await DetailPartialAsync(id, error: "Unable to determine your account.");

        var resent = await workspaceService.ResendInviteAsync(memberId, ownerUserId.Value);
        if (!resent)
            return await DetailPartialAsync(id, error: "Cannot resend invite. The member may have already joined.");

        return await DetailPartialAsync(id, status: "Invitation resent.");
    }

    public async Task<IActionResult> OnGetDetail(long id)
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        var ownerUserId = await identity.ResolveOwnerUserIdAsync(User);
        if (ownerUserId is null)
            return Content("Unauthorized");

        var workspaces = await workspaceService.GetWorkspacesForUserAsync(ownerUserId.Value);
        var workspace = workspaces.FirstOrDefault(w => w.Id == id);
        if (workspace is null)
            return Content("Not found");

        var members = await workspaceService.GetMembersAsync(id);
        return Partial("Shared/_WorkspaceDetail", new WorkspaceDetailViewModel
        {
            WorkspaceId = id,
            WorkspaceSlug = workspace.Slug,
            Members = members
        });
    }


    private async Task<List<Workspace>> LoadWorkspacesAsync()
    {
        var ownerUserId = await identity.ResolveOwnerUserIdAsync(User);
        if (ownerUserId is null)
            return [];

        return await workspaceService.GetWorkspacesForUserAsync(ownerUserId.Value);
    }

    private async Task<IActionResult> ListPartialAsync(string? status = null, string? error = null)
    {
        StatusMessage = status;
        ErrorMessage = error;
        Workspaces = await LoadWorkspacesAsync();
        return Partial("Shared/_WorkspacesList", this);
    }

    private async Task<IActionResult> DetailPartialAsync(long workspaceId, string? status = null, string? error = null)
    {
        StatusMessage = status;
        ErrorMessage = error;
        ExpandedWorkspace = workspaceId.ToString();
        Workspaces = await LoadWorkspacesAsync();
        return Partial("Shared/_WorkspacesList", this);
    }
}
