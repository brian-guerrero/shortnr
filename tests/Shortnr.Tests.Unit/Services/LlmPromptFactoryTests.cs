namespace Shortnr.Tests.Unit.Services;

public class LlmPromptFactoryTests
{
    [Fact]
    public void AnalyzeTraffic_EmbedsCodeAndSummary_AndUsesLowTemperature()
    {
        var request = LlmPromptFactory.AnalyzeTraffic("abc123", "Clicks per day: ...", "gpt-4o-mini");

        Assert.Equal(LlmOperation.AnalyzeTraffic, request.Operation);
        Assert.Equal(0.2, request.Temperature);
        Assert.Contains("abc123", request.UserPrompt);
        Assert.Contains("Clicks per day: ...", request.UserPrompt);
    }

    [Fact]
    public void OptimizeCampaign_EmbedsTagAndSummary()
    {
        var request = LlmPromptFactory.OptimizeCampaign("campaign-summer", "aaa111 | clicks: 40", "gpt-4o-mini");

        Assert.Equal(LlmOperation.OptimizeCampaign, request.Operation);
        Assert.Equal(0.4, request.Temperature);
        Assert.Contains("campaign-summer", request.UserPrompt);
        Assert.Contains("aaa111 | clicks: 40", request.UserPrompt);
    }

    [Fact]
    public void DraftSocialCopy_UsesCreativeTemperature_AndCapsLength()
    {
        var request = LlmPromptFactory.DraftSocialCopy("abc123", "Destination: https://x", "gpt-4o-mini");

        Assert.Equal(LlmOperation.DraftSocialCopy, request.Operation);
        Assert.Equal(0.7, request.Temperature);
        Assert.Equal(256, request.MaxTokens);
        Assert.Contains("280 characters", request.SystemPrompt);
        Assert.Contains("abc123", request.UserPrompt);
    }

    [Fact]
    public void SuggestTags_EmbedsUrl_AndAsksForCommaSeparatedList()
    {
        var request = LlmPromptFactory.SuggestTags("https://example.com/guide?utm_source=x", "gpt-4o-mini");

        Assert.Equal(LlmOperation.SuggestTags, request.Operation);
        Assert.Equal(0.2, request.Temperature);
        Assert.Contains("comma-separated", request.SystemPrompt);
        Assert.Contains("https://example.com/guide", request.UserPrompt);
    }
}