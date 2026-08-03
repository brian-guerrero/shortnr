using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Shortnr.Data.Entities;
using Shortnr.Web.Services;

namespace Shortnr.Web.Pages.Settings;

public class WorkspacesModel : PageModel
{
    private readonly WorkspaceService _workspaceService;
    private readonly UserIdentityService _identity;

    public List<Workspace> Workspaces { get; set; } = [];
    public string? StatusMessage { get; set; }
    public string? ErrorMessage { get; set; }
    public bool IsHtmxRequest { get; set; }
    public string? ExpandedWorkspace { get; set; }

    public WorkspacesModel(WorkspaceService workspaceService, UserIdentityService identity)
    {
        _workspaceService = workspaceService;
        _identity = identity;
    }

    public async Task<IActionResult> OnGet()
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        IsHtmxRequest = Request.Headers["HX-Request"].Count > 0;
        Workspaces = await LoadWorkspacesAsync();
        return Page();
    }

    public async Task<IActionResult> OnPost(string name, string? slug)
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        var ownerUserId = await _identity.ResolveOwnerUserIdAsync(User);
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

        var workspace = await _workspaceService.CreateAsync(name, slug, ownerUserId.Value);
        return await ListPartialAsync(status: $"Workspace '{workspace.Name}' created.");
    }

    public async Task<IActionResult> OnPostDelete(long id)
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        var ownerUserId = await _identity.ResolveOwnerUserIdAsync(User);
        if (ownerUserId is null)
            return await ListPartialAsync(error: "Unable to determine your account.");

        var deleted = await _workspaceService.DeleteWorkspaceAsync(id, ownerUserId.Value);
        if (!deleted)
            return await ListPartialAsync(error: "Cannot delete this workspace. Make sure no links exist in it and you are the owner.");

        return await ListPartialAsync(status: "Workspace deleted.");
    }

    public async Task<IActionResult> OnPostInvite(long id, string email, WorkspaceRole role)
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        var ownerUserId = await _identity.ResolveOwnerUserIdAsync(User);
        if (ownerUserId is null)
            return await DetailPartialAsync(id, error: "Unable to determine your account.");

        email = email.Trim();
        if (email.Length == 0)
            return await DetailPartialAsync(id, error: "Enter an email address.");

        var invited = await _workspaceService.InviteMemberAsync(id, email, role, ownerUserId.Value);
        if (!invited)
            return await DetailPartialAsync(id, error: "Invite failed. Make sure the email isn't already a member and you are the owner.");

        return await DetailPartialAsync(id, status: $"Invitation sent to {email}.");
    }

    public async Task<IActionResult> OnPostSetRole(long id, long userId, WorkspaceRole role)
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        var ownerUserId = await _identity.ResolveOwnerUserIdAsync(User);
        if (ownerUserId is null)
            return await DetailPartialAsync(id, error: "Unable to determine your account.");

        var updated = await _workspaceService.SetRoleAsync(id, userId, role, ownerUserId.Value);
        if (!updated)
            return await DetailPartialAsync(id, error: "Cannot change this member's role.");

        return await DetailPartialAsync(id, status: "Role updated.");
    }

    public async Task<IActionResult> OnPostRemoveMember(long id, long userId)
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        var ownerUserId = await _identity.ResolveOwnerUserIdAsync(User);
        if (ownerUserId is null)
            return await DetailPartialAsync(id, error: "Unable to determine your account.");

        var removed = await _workspaceService.RemoveMemberAsync(id, userId, ownerUserId.Value);
        if (!removed)
            return await DetailPartialAsync(id, error: "Cannot remove this member. A workspace must keep at least one owner.");

        return await DetailPartialAsync(id, status: "Member removed.");
    }

    public async Task<IActionResult> OnGetDetail(long id)
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        var ownerUserId = await _identity.ResolveOwnerUserIdAsync(User);
        if (ownerUserId is null)
            return Content("Unauthorized");

        var workspace = await _workspaceService.GetWorkspaceBySlugAsync(id.ToString());
        if (workspace is null)
        {
            var workspaces = await _workspaceService.GetWorkspacesForUserAsync(ownerUserId.Value);
            workspace = workspaces.FirstOrDefault(w => w.Id == id);
        }
        if (workspace is null)
            return Content("Not found");

        var members = await _workspaceService.GetMembersAsync(id);
        ViewData["Members"] = members;
        ViewData["WorkspaceId"] = id;
        ViewData["WorkspaceSlug"] = workspace.Slug;
        return Partial("Shared/_WorkspaceDetail");
    }

    private IActionResult? EnforceAccess()
    {
        if (_identity.IsAuthEnabled && User.Identity?.IsAuthenticated != true)
            return Request.Headers["HX-Request"].Count > 0
                ? Unauthorized()
                : RedirectToPage("/Index");

        return null;
    }

    private async Task<List<Workspace>> LoadWorkspacesAsync()
    {
        var ownerUserId = await _identity.ResolveOwnerUserIdAsync(User);
        if (ownerUserId is null)
            return [];

        return await _workspaceService.GetWorkspacesForUserAsync(ownerUserId.Value);
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
