namespace Shortnr.Web.Features.Infrastructure;

/// <summary>
/// Backs the slide-in toast that confirms a bulk-action result (PRD-019). It
/// travels out-of-band into <c>#toast-region</c> from whichever partial the
/// action's target swapped, so the toast is never nested inside the swapped
/// table markup.
/// </summary>
public class BulkToastViewModel
{
    public required string Message { get; init; }
    public StatusKind Kind { get; init; } = StatusKind.Info;

    /// <summary>When set (bulk delete only), the toast shows an Undo button.</summary>
    public string? UndoToken { get; init; }
}
