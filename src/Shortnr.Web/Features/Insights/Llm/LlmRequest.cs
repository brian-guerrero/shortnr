namespace Shortnr.Web.Features.Insights.Llm;

/// <summary>A single chat-completion call to the configured AI provider.</summary>
public sealed record LlmRequest(
    LlmOperation Operation,
    string SystemPrompt,
    string UserPrompt,
    string Model,
    double Temperature,
    int? MaxTokens);