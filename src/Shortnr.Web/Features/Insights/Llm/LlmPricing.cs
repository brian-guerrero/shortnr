using Microsoft.Extensions.Options;

namespace Shortnr.Web.Features.Insights.Llm;

/// <summary>
/// Estimates the USD cost of a call from token usage so the optional monthly
/// budget can be enforced. Uses a small built-in table of OpenAI/Anthropic
/// list prices (USD per 1M tokens), overridable via the
/// <c>AiInsights:Llm:InputPricePerMillion</c>/<c>OutputPricePerMillion</c> config.
/// Models not in the table price at $0 unless the config override is set.
/// </summary>
public sealed class LlmPricing(IOptions<LlmOptions> options)
{
    public decimal EstimateCost(int inputTokens, int outputTokens, string model)
    {
        var (inputRate, outputRate) = ResolveRates(model);
        return inputTokens / 1_000_000m * inputRate + outputTokens / 1_000_000m * outputRate;
    }

    private (decimal Input, decimal Output) ResolveRates(string model)
    {
        var cfg = options.Value;
        if (cfg.InputPricePerMillion > 0 && cfg.OutputPricePerMillion > 0)
            return (cfg.InputPricePerMillion, cfg.OutputPricePerMillion);

        var name = (model ?? string.Empty).ToLowerInvariant();
        return Rates.FirstOrDefault(r => name.Contains(r.Key, StringComparison.Ordinal)) is var hit
            ? (hit.Input, hit.Output)
            : (0m, 0m);
    }

    /// <summary>Rough prompt-token estimate (chars / 4), used for the pre-call budget check.</summary>
    public static int EstimatePromptTokens(string text) =>
        (int)Math.Ceiling((text ?? string.Empty).Length / 4.0);

    // Ordered most-specific-first so e.g. "gpt-4o-mini" isn't caught by "gpt-4o".
    private static readonly (string Key, decimal Input, decimal Output)[] Rates =
    [
        ("gpt-4o-mini", 0.15m, 0.60m),
        ("gpt-4o", 2.50m, 10.00m),
        ("gpt-4.1-nano", 0.10m, 0.40m),
        ("gpt-4.1-mini", 0.40m, 1.60m),
        ("gpt-4.1", 2.00m, 8.00m),
        ("gpt-4-turbo", 10.00m, 30.00m),
        ("gpt-4", 30.00m, 60.00m),
        ("gpt-3.5-turbo", 0.50m, 1.50m),
        ("o3-mini", 1.10m, 4.40m),
        // Anthropic family tokens match the hyphenated ids, e.g. claude-3-5-sonnet.
        ("opus", 15.00m, 75.00m),
        ("sonnet", 3.00m, 15.00m),
        ("haiku", 0.25m, 1.25m)
    ];
}