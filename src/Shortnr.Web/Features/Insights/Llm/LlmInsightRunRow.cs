namespace Shortnr.Web.Features.Insights.Llm;

/// <summary>Display model for a single persisted <c>LlmInsightRun</c> row on the /insights history list.</summary>
public class LlmInsightRunRow
{
    public long Id { get; init; }
    public LlmOperation Operation { get; init; }
    public string InputSummary { get; init; } = "";
    public bool Success { get; init; }
    public string? Content { get; init; }
    public string? FriendlyMessage { get; init; }
    public DateTime CreatedAtUtc { get; init; }

    public string OperationLabel => Operation switch
    {
        LlmOperation.AnalyzeTraffic => "Analyze traffic",
        LlmOperation.OptimizeCampaign => "Optimize campaign",
        LlmOperation.DraftSocialCopy => "Draft social copy",
        LlmOperation.SuggestTags => "Suggest tags",
        _ => Operation.ToString()
    };
}
