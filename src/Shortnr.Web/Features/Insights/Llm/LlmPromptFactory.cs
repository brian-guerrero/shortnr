namespace Shortnr.Web.Features.Insights.Llm;

/// <summary>
/// Builds the system/user prompts for the four PRD-023 analysis operations.
/// Every prompt instructs plain-text output and frames findings as suggestions
/// to verify, matching the "AI-generated, verify before publishing" UI disclaimer.
/// </summary>
public static class LlmPromptFactory
{
    public static LlmRequest AnalyzeTraffic(string shortCode, string linkSummary, string model) => new(
        LlmOperation.AnalyzeTraffic,
        "You are the analytics assistant for shortnr, a self-hosted URL shortener. "
        + "Explain what appears to have driven the clicks on this short link in 3-5 terse "
        + "bullet lines using only the provided data. Frame each point as a likely cause, "
        + "never a certainty. Plain text, no markdown symbols, no emoji.",
        $"Short link code: {shortCode}\n\nClick data:\n{linkSummary}",
        model, 0.2, 512);

    public static LlmRequest OptimizeCampaign(string tag, string campaignSummary, string model) => new(
        LlmOperation.OptimizeCampaign,
        "You are a marketing analyst for shortnr. Given click data for one campaign's short "
        + "links, suggest 3-5 concrete, testable optimizations that reference the data (best "
        + "channels, strong and weak links). Plain text bullets, no markdown symbols, no emoji.",
        $"Campaign tag: {tag}\n\nCampaign link data:\n{campaignSummary}",
        model, 0.4, 512);

    public static LlmRequest DraftSocialCopy(string shortCode, string linkSummary, string model) => new(
        LlmOperation.DraftSocialCopy,
        "You are a social media copywriter. Draft 3 short social posts, one per line, each "
        + "at most 280 characters, promoting the given short link. Keep the tone natural and "
        + "match the site vibes conveyed by the click data. Plain text only.",
        $"Short link code: {shortCode}\n\nContext:\n{linkSummary}",
        model, 0.7, 256);

    public static LlmRequest SuggestTags(string url, string model) => new(
        LlmOperation.SuggestTags,
        "You suggest URL tags. Given a destination URL, return up to 8 tags as a single "
        + "comma-separated line. Each tag is lowercase, 1-2 words joined with a dash, using "
        + "letters, digits and dashes only. Ignore tracking parameters (utm_*). Output only "
        + "the tags.",
        $"URL: {url}",
        model, 0.2, 80);
}