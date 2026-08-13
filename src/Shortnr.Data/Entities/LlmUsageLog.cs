namespace Shortnr.Data.Entities;

/// <summary>
/// One row per LLM API call made by the PRD-023 insights layer. Written
/// synchronously by <c>LlmUsageService</c> right after each call so the monthly
/// budget (<c>AiInsights:Llm:MonthlyBudget</c>) is enforced from durable data.
/// Kept personal-only like <c>AiActivityLog</c>; the budget itself is instance
/// wide because the provider API key lives in instance config.
/// </summary>
public class LlmUsageLog
{
    public long Id { get; set; }
    /// <summary>The user who triggered the call, when auth is enabled.</summary>
    public long? OwnerUserId { get; set; }
    /// <summary>Provider name (OpenAI, Anthropic, OpenRouter, Ollama).</summary>
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    /// <summary>Stable operation name, e.g. <c>analyze</c> or <c>draft-social-copy</c>.</summary>
    public string Operation { get; set; } = string.Empty;
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    /// <summary>Estimated USD cost of the call, 0 when no pricing info applies.</summary>
    public decimal EstimatedCostUsd { get; set; }
    public bool Success { get; set; }
    /// <summary>Set when the call failed: rate_limit, auth, timeout, upstream, ...</summary>
    public string? ErrorKind { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public User? Owner { get; set; }
}