using Shortnr.Data.Entities;

namespace Shortnr.Web.Features.Insights;

/// <summary>Display model for a single pending <c>TagSuggestion</c> row on the /insights page.</summary>
public class InsightSuggestionRow
{
    public long Id { get; init; }
    public string ShortCode { get; init; } = "";
    public string SuggestedTag { get; init; } = "";
    public TagSuggestionSource Source { get; init; }
    public long ClickCount { get; init; }
    public DateTime FirstObservedUtc { get; init; }
    public DateTime CreatedAtUtc { get; init; }

    public string SourceLabel => Source switch
    {
        TagSuggestionSource.ReferrerDomainCluster => "Top referrer",
        TagSuggestionSource.UtmExtraction => "UTM campaign",
        TagSuggestionSource.HighFrequency => "High traffic",
        _ => Source.ToString()
    };
}
