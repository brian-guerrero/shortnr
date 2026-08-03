using Shortnr.Data.Entities;

namespace Shortnr.Web.Pages.Settings;

public class WorkspaceDetailViewModel
{
    public long WorkspaceId { get; init; }
    public string WorkspaceSlug { get; init; } = string.Empty;
    public List<WorkspaceMember> Members { get; init; } = [];
}
