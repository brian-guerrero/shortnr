namespace Shortnr.Web.Features.Infrastructure;

/// <summary>
/// Model for the shared <c>Shared/_EmptyState</c> partial — the "nothing here yet" copy shown
/// in place of a list or table.
/// </summary>
public class EmptyStateViewModel
{
    public required string Message { get; init; }

    /// <summary>Wraps the message in the <c>article.ai-activity</c> card used on the activity/insights surfaces.</summary>
    public bool Boxed { get; init; }

    public string? CssClass { get; init; }

    public string? Style { get; init; }
}
