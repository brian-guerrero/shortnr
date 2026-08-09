namespace Shortnr.Data.Entities;

/// <summary>
/// Which local heuristic produced a <see cref="TagSuggestion"/>. Recorded so the
/// insights page can label where a suggestion came from.
/// </summary>
public enum TagSuggestionSource
{
    /// <summary>A single referrer domain accounted for a disproportionate share of a link's clicks.</summary>
    ReferrerDomainCluster = 0,
    /// <summary>The link's flattened URL carried a recognizable UTM campaign parameter.</summary>
    UtmExtraction = 1,
    /// <summary>The link's click rate within the analysis window crossed the high-frequency threshold.</summary>
    HighFrequency = 2
}
