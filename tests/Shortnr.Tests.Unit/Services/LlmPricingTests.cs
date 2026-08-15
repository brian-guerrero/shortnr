using Microsoft.Extensions.Options;

namespace Shortnr.Tests.Unit.Services;

public class LlmPricingTests
{
    private static LlmPricing Build(LlmOptions options) => new(Options.Create(options));

    [Fact]
    public void EstimateCost_Gpt4oMini_AppliesMiniRates()
    {
        var pricing = Build(new LlmOptions { Enabled = true });

        // 1M input @ $0.15 + 1M output @ $0.60
        var cost = pricing.EstimateCost(1_000_000, 1_000_000, "gpt-4o-mini");

        Assert.Equal(0.75m, cost);
    }

    [Fact]
    public void EstimateCost_Gpt4o_IsNotMistakenForGpt4oMini()
    {
        var pricing = Build(new LlmOptions { Enabled = true });

        var cost = pricing.EstimateCost(1_000_000, 1_000_000, "gpt-4o");

        Assert.Equal(12.5m, cost); // $2.50 + $10.00
    }

    [Fact]
    public void EstimateCost_ClaudeSonnet_AppliesAnthropicRates()
    {
        var pricing = Build(new LlmOptions { Enabled = true });

        var cost = pricing.EstimateCost(1_000_000, 1_000_000, "claude-3-5-sonnet");

        Assert.Equal(18m, cost); // $3 + $15
    }

    [Fact]
    public void EstimateCost_UnknownModel_PricesAtZero()
    {
        var pricing = Build(new LlmOptions { Enabled = true });

        Assert.Equal(0m, pricing.EstimateCost(50_000, 50_000, "some-custom-llama3"));
    }

    [Fact]
    public void EstimateCost_ConfigOverride_AppliesToAllModels()
    {
        var pricing = Build(new LlmOptions
        {
            Enabled = true,
            InputPricePerMillion = 10m,
            OutputPricePerMillion = 20m
        });

        var cost = pricing.EstimateCost(500_000, 500_000, "whatever");

        Assert.Equal(15m, cost);
    }

    [Fact]
    public void EstimateCost_TokenAndCost_AreProportional()
    {
        var pricing = Build(new LlmOptions { Enabled = true });

        var full = pricing.EstimateCost(1_000_000, 0, "gpt-4o-mini");
        var tenth = pricing.EstimateCost(100_000, 0, "gpt-4o-mini");

        Assert.Equal(full / 10, tenth);
    }

    [Fact]
    public void EstimatePromptTokens_UsesFourthOfCharacterLength()
    {
        Assert.Equal(1, LlmPricing.EstimatePromptTokens("abc"));
        Assert.Equal(3, LlmPricing.EstimatePromptTokens("abcdefghijkl"));
    }
}