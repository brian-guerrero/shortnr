namespace Shortnr.Data.Entities;

/// <summary>
/// A tag the AI insights background service proposed for a <see cref="ShortenedUrl"/>
/// based on local click-pattern heuristics. The owner accepts (which writes a
/// <see cref="ShortenedUrlTag"/>) or dismisses the suggestion; dismissed rows are
/// never re-suggested.
/// </summary>
public class TagSuggestion
{
    public long Id { get; set; }
    public long ShortenedUrlId { get; set; }
    public string SuggestedTag { get; set; } = string.Empty;
    public TagSuggestionSource Source { get; set; }
    /// <summary>Number of clicks the heuristic attributed to this pattern.</summary>
    public long ClickCount { get; set; }
    public DateTime FirstObservedUtc { get; set; }
    public TagSuggestionStatus Status { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public ShortenedUrl ShortenedUrl { get; set; } = null!;
}
