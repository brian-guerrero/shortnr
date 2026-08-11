namespace Shortnr.Data.Entities;

/// <summary>
/// Lifecycle of a <see cref="TagSuggestion"/> row: created as <see cref="Pending"/>,
/// then either applied to the link (<see cref="Accepted"/>) or rejected by the owner
/// (<see cref="Dismissed"/>). Dismissed rows are never re-suggested.
/// </summary>
public enum TagSuggestionStatus
{
    Pending = 0,
    Accepted = 1,
    Dismissed = 2
}
