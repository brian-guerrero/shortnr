using System.ClientModel;
using System.Net;
using Microsoft.Extensions.AI;

namespace Shortnr.Web.Features.Insights.Llm;

/// <summary>
/// Wraps whichever provider's <see cref="IChatClient"/> <c>AiInsightsModule</c> selected and is
/// the single place that turns provider-specific exceptions into <see cref="LlmException"/>/
/// <see cref="LlmErrorKind"/> -- the direct replacement for the old <c>LlmClient.BuildHttpError</c>
/// switch, just re-triggered from whatever exception type each SDK throws instead of a raw
/// <see cref="HttpResponseMessage"/>. Every provider funnels through this one status-to-kind
/// mapping regardless of which SDK it came from.
/// </summary>
public sealed class LlmErrorTranslatingChatClient(IChatClient innerClient, ILogger<LlmErrorTranslatingChatClient> logger)
    : DelegatingChatClient(innerClient)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.GetResponseAsync(messages, options, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("LLM call timed out");
            throw new LlmException("The AI provider timed out.", LlmErrorKind.Timeout);
        }
        catch (AnthropicHttpException ex) // the hand-written Anthropic adapter
        {
            logger.LogWarning(ex, "LLM call failed with HTTP {Status}", (int)ex.StatusCode);
            throw MapHttpStatus(ex.StatusCode, ex.Message);
        }
        catch (ClientResultException ex) // OpenAI SDK (System.ClientModel), surfaced via Microsoft.Extensions.AI.OpenAI's IChatClient adapter
        {
            logger.LogWarning(ex, "LLM call failed with HTTP {Status}", ex.Status);
            throw MapHttpStatus((HttpStatusCode)ex.Status, ex.Message);
        }
        catch (HttpRequestException ex) // OllamaSharp / raw network failures
        {
            logger.LogWarning(ex, "LLM call network failure");
            throw ex.StatusCode is { } status
                ? MapHttpStatus(status, ex.Message)
                : new LlmException($"Could not reach the AI provider: {ex.Message}", LlmErrorKind.Network, ex);
        }
    }

    // Mirrors the old LlmClient.BuildHttpError 1:1: the message here only ever surfaces in logs
    // (LlmInsightService logs the caught LlmException) -- the actual UI-facing copy always comes
    // from LlmException.FriendlyMessage, which switches on Kind independently of this message.
    private static LlmException MapHttpStatus(HttpStatusCode status, string? message)
    {
        var kind = status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => LlmErrorKind.Auth,
            HttpStatusCode.TooManyRequests => LlmErrorKind.RateLimit,
            HttpStatusCode.PaymentRequired => LlmErrorKind.PaymentRequired,
            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => LlmErrorKind.BadRequest,
            HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout => LlmErrorKind.Timeout,
            _ => LlmErrorKind.Upstream
        };
        return new LlmException(message ?? $"The AI provider returned HTTP {(int)status}.", kind);
    }
}
