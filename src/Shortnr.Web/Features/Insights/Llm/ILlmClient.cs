namespace Shortnr.Web.Features.Insights.Llm;

/// <summary>
/// Low-level chat-completion transport abstraction. Implemented by
/// <see cref="LlmClient"/> using direct HTTP against the provider's API; also the
/// seam integration tests stub in place of a live provider.
/// </summary>
public interface ILlmClient
{
    Task<LlmCompletion> CompleteAsync(LlmRequest request, CancellationToken ct = default);
}