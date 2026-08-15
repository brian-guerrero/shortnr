namespace Shortnr.Web.Features.Insights.Llm;

/// <summary>AI provider backends supported by the PRD-023 insights layer.</summary>
public enum LlmProvider
{
    OpenAi = 0,
    Anthropic = 1,
    OpenRouter = 2,
    Ollama = 3
}