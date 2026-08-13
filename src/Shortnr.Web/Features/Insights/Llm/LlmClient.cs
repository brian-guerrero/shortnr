using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Shortnr.Web.Features.Insights.Llm;

/// <summary>
/// HTTP chat-completion transport for all four PRD-023 providers. OpenAI,
/// OpenRouter and Ollama all speak the OpenAI-compatible <c>POST /chat/completions</c>
/// contract; Anthropic speaks <c>POST /v1/messages</c>. Built on plain
/// <see cref="HttpClient"/> (no SDK packages) so every provider shares one code
/// path, one timeout policy and one token-usage parser, and so tests can stub the
/// transport with a fake <see cref="HttpMessageHandler"/>.
/// </summary>
public class LlmClient(HttpClient httpClient, IOptions<LlmOptions> options, ILogger<LlmClient> logger) : ILlmClient
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public async Task<LlmCompletion> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        var cfg = options.Value;
        var (uri, headers, body) = cfg.Provider == LlmProvider.Anthropic
            ? BuildAnthropicRequest(cfg, request)
            : BuildOpenAiCompatibleRequest(cfg, request);

        using var message = new HttpRequestMessage(HttpMethod.Post, uri);
        foreach (var (key, value) in headers)
            message.Headers.TryAddWithoutValidation(key, value);
        message.Content = new StringContent(body, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(message, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("LLM {Operation} timed out", request.Operation);
            throw new LlmException("The AI provider timed out.", LlmErrorKind.Timeout);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "LLM {Operation} network failure", request.Operation);
            throw new LlmException($"Could not reach the AI provider: {ex.Message}", LlmErrorKind.Network, ex);
        }

        using (response)
        {
            var payload = await ReadPayloadAsync(response, ct);
            if (!response.IsSuccessStatusCode)
                throw BuildHttpError(response.StatusCode, payload);

            if (payload.ValueKind == JsonValueKind.Undefined)
                throw new LlmException("The AI provider returned an unreadable response.", LlmErrorKind.InvalidResponse);

            return ParseCompletion(payload, cfg.Provider, request.Model);
        }
    }

    // -------------------------------------------------------------------------
    // Request builders
    // -------------------------------------------------------------------------

    private static (string Uri, List<(string Key, string Value)> Headers, string Body) BuildOpenAiCompatibleRequest(
        LlmOptions cfg, LlmRequest request)
    {
        var baseUrl = cfg.Provider switch
        {
            LlmProvider.OpenRouter => string.IsNullOrEmpty(cfg.BaseUrl) ? "https://openrouter.ai/api/v1" : TrimSlash(cfg.BaseUrl),
            // Ollama's OpenAI-compatible endpoint lives under /v1; ensure it's present
            // whether the operator configured a bare host or the full /v1 path.
            LlmProvider.Ollama => EnsureV1(string.IsNullOrEmpty(cfg.BaseUrl) ? "http://localhost:11434" : TrimSlash(cfg.BaseUrl)),
            _ => string.IsNullOrEmpty(cfg.BaseUrl) ? "https://api.openai.com/v1" : TrimSlash(cfg.BaseUrl)
        };

        var headers = new List<(string, string)>();
        if (!string.IsNullOrEmpty(cfg.ApiKey))
            headers.Add(("Authorization", $"Bearer {cfg.ApiKey}"));
        if (cfg.Provider == LlmProvider.OpenRouter)
        {
            headers.Add(("HTTP-Referer", "https://github.com/shortnr/shortnr"));
            headers.Add(("X-Title", "shortnr AI insights"));
        }

        var body = JsonSerializer.Serialize(new
        {
            model = request.Model,
            messages = new object[]
            {
                new { role = "system", content = request.SystemPrompt },
                new { role = "user", content = request.UserPrompt }
            },
            temperature = request.Temperature,
            max_tokens = request.MaxTokens ?? 512
        }, Web);

        return ($"{baseUrl}/chat/completions", headers, body);
    }

    private static (string Uri, List<(string Key, string Value)> Headers, string Body) BuildAnthropicRequest(
        LlmOptions cfg, LlmRequest request)
    {
        var baseUrl = string.IsNullOrEmpty(cfg.BaseUrl) ? "https://api.anthropic.com" : TrimSlash(cfg.BaseUrl);
        var headers = new List<(string Key, string Value)>
        {
            ("x-api-key", cfg.ApiKey),
            ("anthropic-version", "2023-06-01")
        };

        var body = JsonSerializer.Serialize(new
        {
            model = request.Model,
            max_tokens = request.MaxTokens ?? 512,
            system = request.SystemPrompt,
            messages = new object[]
            {
                new { role = "user", content = request.UserPrompt }
            },
            temperature = request.Temperature
        }, Web);

        return ($"{baseUrl}/v1/messages", headers, body);
    }

    private static string TrimSlash(string url) => url.TrimEnd('/');

    private static string EnsureV1(string baseUrl) =>
        baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? baseUrl : $"{baseUrl}/v1";

    // -------------------------------------------------------------------------
    // Response parsing
    // -------------------------------------------------------------------------

    private static LlmCompletion ParseCompletion(JsonElement payload, LlmProvider provider, string requestedModel)
    {
        var model = TryGetString(payload, "model") ?? requestedModel;
        string text;
        if (provider == LlmProvider.Anthropic)
        {
            text = string.Join("\n", payload.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array
                ? content.EnumerateArray()
                    .Where(t => t.TryGetProperty("type", out var type) && type.GetString() == "text")
                    .Select(t => TryGetString(t, "text") ?? string.Empty)
                : []);
        }
        else
        {
            text = payload.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array
                   && choices.GetArrayLength() > 0
                ? choices[0].TryGetProperty("message", out var message) ? TryGetString(message, "content") ?? string.Empty : string.Empty
                : string.Empty;
        }

        var (promptTokens, completionTokens) = ParseUsage(payload);
        return new LlmCompletion(text, promptTokens, completionTokens, model);
    }

    private static (int Prompt, int Completion) ParseUsage(JsonElement payload)
    {
        if (!payload.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
            return (0, 0);

        var prompt = TryGetInt(usage, "prompt_tokens") ?? TryGetInt(usage, "input_tokens") ?? 0;
        var completion = TryGetInt(usage, "completion_tokens") ?? TryGetInt(usage, "output_tokens") ?? 0;
        return (prompt, completion);
    }

    private static LlmException BuildHttpError(HttpStatusCode status, JsonElement payload)
    {
        var message = ExtractErrorMessage(payload);
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

    private static string? ExtractErrorMessage(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object) return null;
        if (TryGetString(payload, "message") is { } top) return top;
        if (payload.TryGetProperty("error", out var error))
            return TryGetString(error, "message");
        return null;
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