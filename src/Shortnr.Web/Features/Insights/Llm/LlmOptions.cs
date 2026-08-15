namespace Shortnr.Web.Features.Insights.Llm;

/// <summary>
/// Configuration for the optional LLM analysis layer on top of the PRD-006
/// heuristics. Reads from the <c>AiInsights:Llm</c> config section. The layer is
/// off by default: when <see cref="Enabled"/> is false the system behaves
/// exactly like PRD-006 (deterministic heuristics only).
/// </summary>
public class LlmOptions
{
    public bool Enabled { get; set; }

    public LlmProvider Provider { get; set; } = LlmProvider.OpenAi;

    /// <summary>BYO API key. Not needed for a local Ollama instance.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Model name sent to the provider (e.g. <c>gpt-4o-mini</c>, <c>claude-3-5-sonnet</c>,
    /// <c>llama3.1</c>). Empty means AI analysis is disabled with a friendly message.
    /// </summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// Base URL override. Used for local OpenAI-compatible servers and Ollama
    /// (default <c>http://localhost:11434</c>).
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Maximum estimated spend per calendar month in USD. 0 (default) means
    /// unlimited. Enforced before each call using an estimate of prompt tokens.
    /// </summary>
    public decimal MonthlyBudget { get; set; }

    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Optional override of the per-model price table (USD per 1M tokens). When
    /// both are &gt; 0 they apply to every model; otherwise the built-in table is
    /// used and unknown models price at 0 (not counted toward the budget).
    /// </summary>
    public decimal InputPricePerMillion { get; set; }
    public decimal OutputPricePerMillion { get; set; }
}