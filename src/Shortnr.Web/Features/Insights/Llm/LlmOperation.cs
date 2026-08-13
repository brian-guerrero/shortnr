namespace Shortnr.Web.Features.Insights.Llm;

/// <summary>The four analysis operations exposed on the /insights page.</summary>
public enum LlmOperation
{
    AnalyzeTraffic = 0,
    OptimizeCampaign = 1,
    DraftSocialCopy = 2,
    SuggestTags = 3
}