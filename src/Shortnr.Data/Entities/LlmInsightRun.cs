namespace Shortnr.Data.Entities;

/// <summary>
/// One row per PRD-023 "Ask AI" invocation on the /insights page -- persists the operation,
/// its input and outcome so a page refresh (or later visit) still shows history instead of
/// just the most recent htmx swap. Kept personal-only like <c>AiActivityLog</c>/<c>LlmUsageLog</c>.
/// Distinct from <c>LlmUsageLog</c>: that table is the cost/budget audit trail (tokens,
/// estimated cost) written by <c>LlmUsageService</c> regardless of UI; this one is the
/// user-facing content history written by the /insights page model itself. "NotFound"
/// outcomes (bad short code/tag/URL, caught before ever calling the provider) aren't
/// persisted here -- there's nothing worth showing in history for a local validation miss.
/// </summary>
public class LlmInsightRun
{
    public long Id { get; set; }
    public long? OwnerUserId { get; set; }
    /// <summary>One of the four <c>LlmOperation</c> values, e.g. <c>AnalyzeTraffic</c>.</summary>
    public string Operation { get; set; } = string.Empty;
    /// <summary>What the user entered: a short code, campaign tag, or destination URL.</summary>
    public string InputSummary { get; set; } = string.Empty;
    public bool Success { get; set; }
    /// <summary>Generated text (success only).</summary>
    public string? Content { get; set; }
    /// <summary>Friendly explanation shown for a non-success outcome.</summary>
    public string? FriendlyMessage { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public User? Owner { get; set; }
}
