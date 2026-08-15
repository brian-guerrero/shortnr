using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Shortnr.Web.Features.Insights.Llm;

/// <summary>
/// The one remaining hand-written provider transport in the PRD-023 LLM layer: there is no
/// first-party Microsoft.Extensions.AI/Aspire package for Anthropic, unlike OpenAI/OpenRouter
/// (<see cref="Microsoft.Extensions.AI.OpenAIClientExtensions.AsIChatClient(OpenAI.Chat.ChatClient)"/>)
/// and Ollama (<c>OllamaSharp.OllamaApiClient</c>, which implements <see cref="IChatClient"/>
/// directly). Implements <see cref="IChatClient"/> itself rather than deriving from
/// <see cref="DelegatingChatClient"/> because it *is* the transport, not a wrapper around one.
/// Non-2xx responses throw <see cref="AnthropicHttpException"/>, not <see cref="LlmException"/> --
/// translation to the app's error taxonomy is centralized one layer up in
/// <see cref="LlmErrorTranslatingChatClient"/> so all four providers funnel through one mapping.
/// </summary>
public sealed class AnthropicChatClient(HttpClient httpClient, string apiKey, string? baseUrl) : IChatClient
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var messageList = messages as IReadOnlyList<ChatMessage> ?? messages.ToList();
        var systemPrompt = messageList.FirstOrDefault(m => m.Role == ChatRole.System)?.Text ?? string.Empty;
        var conversation = messageList
            .Where(m => m.Role != ChatRole.System)
            .Select(m => new { role = m.Role == ChatRole.Assistant ? "assistant" : "user", content = m.Text });

        var body = JsonSerializer.Serialize(new
        {
            model = options?.ModelId,
            max_tokens = options?.MaxOutputTokens ?? 512,
            system = systemPrompt,
            messages = conversation,
            temperature = options?.Temperature
        }, Web);

        var uri = $"{(string.IsNullOrEmpty(baseUrl) ? "https://api.anthropic.com" : baseUrl.TrimEnd('/'))}/v1/messages";
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var payload = await ReadPayloadAsync(response, cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new AnthropicHttpException(response.StatusCode, ExtractErrorMessage(payload));

        if (payload.ValueKind == JsonValueKind.Undefined)
            throw new AnthropicHttpException(response.StatusCode, "The AI provider returned an unreadable response.");

        return ParseResponse(payload);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Streaming is not used by the /insights AI operations.");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    // HttpClient is owned by IHttpClientFactory (AddHttpClient in AiInsightsModule) -- don't dispose it here.
    public void Dispose()
    {
    }

    private static ChatResponse ParseResponse(JsonElement payload)
    {
        var model = TryGetString(payload, "model") ?? string.Empty;
        var text = string.Join("\n", payload.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array
            ? content.EnumerateArray()
                .Where(t => t.TryGetProperty("type", out var type) && type.GetString() == "text")
                .Select(t => TryGetString(t, "text") ?? string.Empty)
            : []);

        var (inputTokens, outputTokens) = ParseUsage(payload);

        return new ChatResponse(new ChatMessage(ChatRole.Assistant, text))
        {
            ModelId = model,
            Usage = new UsageDetails { InputTokenCount = inputTokens, OutputTokenCount = outputTokens }
        };
    }

    private static (long Input, long Output) ParseUsage(JsonElement payload)
    {
        if (!payload.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
            return (0, 0);

        return (TryGetInt(usage, "input_tokens") ?? 0, TryGetInt(usage, "output_tokens") ?? 0);
    }

    private static string? ExtractErrorMessage(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object) return null;
        return payload.TryGetProperty("error", out var error) ? TryGetString(error, "message") : null;
    }

    private static string? TryGetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? TryGetInt(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var number)
            ? number
            : null;

    private static async Task<JsonElement> ReadPayloadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        }
        catch (JsonException)
        {
            return default;
        }
    }
}

/// <summary>
/// Thrown by <see cref="AnthropicChatClient"/> on a non-2xx response. Caught and mapped to
/// <see cref="LlmException"/>/<see cref="LlmErrorKind"/> by <see cref="LlmErrorTranslatingChatClient"/>,
/// the single place that knows how to turn every provider's failure shape into the app's error taxonomy.
/// </summary>
public sealed class AnthropicHttpException(HttpStatusCode statusCode, string? message) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}
