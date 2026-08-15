using Shortnr.Web.Features.Authentication;

namespace Shortnr.Web.Features.Infrastructure;

/// <summary>
/// Model for the shared <c>Shared/_PageHeader</c> partial. A page has either a
/// <see cref="Subtitle"/> (rendered as an <c>hgroup</c>) or an active <see cref="Workspace"/>
/// (rendered as a trailing badge) — no page currently needs both.
/// </summary>
public class PageHeaderViewModel
{
    public required string Title { get; init; }

    public string? Subtitle { get; init; }

    public ActiveWorkspaceContext? Workspace { get; init; }
}
