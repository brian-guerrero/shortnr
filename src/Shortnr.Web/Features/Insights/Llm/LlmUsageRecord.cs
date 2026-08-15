namespace Shortnr.Web.Features.Insights.Llm;

/// <summary>One LLM call to persist in <c>LlmUsageLog</c> for cost tracking.</summary>
public sealed record LlmUsageRecord(
    string Provider,
    string Model,
    string Operation,
    int PromptTokens,
    int CompletionTokens,
    decimal EstimatedCostUsd,
    bool Success,
    string? ErrorKind,
    long? OwnerUserId);