using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Shortnr.Tests.Unit.Services;

public class LlmClientTests
{
    private static LlmClient BuildClient(LlmOptions options, StubHandler handler)
    {
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        return new LlmClient(http, Options.Create(options), NullLogger<LlmClient>.Instance);
    }

    private static LlmRequest Req(string model = "gpt-4o-mini") =>
        new(LlmOperation.AnalyzeTraffic, "system prompt", "user prompt", model, 0.2, 512);

    private static LlmOptions OpenAiOptions(string key = "test-key", string model = "gpt-4o-mini") => new()
    {
        Enabled = true,
        Provider = LlmProvider.OpenAi,
        ApiKey = key,
        Model = model
    };

    // -------------------------------------------------------------------------
    // OpenAI (and OpenAI-compatible) transport
    // -------------------------------------------------------------------------

    [Fact]
    public async Task OpenAi_CallsChatCompletions_WithBearerAuth_AndParsesResponse()
    {
        var handler = new StubHandler(_ => JsonResponse("""
            {
              "model": "gpt-4o-mini",
              "choices": [{ "message": { "role": "assistant", "content": "Traffic looks organic." } }],
              "usage": { "prompt_tokens": 120, "completion_tokens": 45 }
            }
            """));
        var client = BuildClient(OpenAiOptions(), handler);

        var completion = await client.CompleteAsync(Req());

        Assert.Equal("https://api.openai.com/v1/chat/completions", handler.LastRequest?.RequestUri?.AbsoluteUri);
        Assert.Equal(HttpMethod.Post, handler.LastRequest?.Method);
        Assert.Equal("Bearer test-key", handler.LastRequest?.Headers.Authorization?.ToString());
        Assert.Equal(120, completion.PromptTokens);
        Assert.Equal(45, completion.CompletionTokens);
        Assert.Equal("Traffic looks organic.", completion.Text);
        Assert.Equal("gpt-4o-mini", completion.Model);

        using var body = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal("gpt-4o-mini", body.RootElement.GetProperty("model").GetString());
        Assert.Equal("system prompt", body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task OpenAi_WhenUsageMissing_DefaultsTokensToZero()
    {
        var handler = new StubHandler(_ => JsonResponse("""
            { "model": "gpt-4o-mini", "choices": [{ "message": { "content": "ok" } }] }
            """));
        var client = BuildClient(OpenAiOptions(), handler);

        var completion = await client.CompleteAsync(Req());

        Assert.Equal(0, completion.PromptTokens);
        Assert.Equal(0, completion.CompletionTokens);
    }

    // -------------------------------------------------------------------------
    // Anthropic transport
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Anthropic_CallsV1Messages_WithVersionHeaders_AndParsesResponse()
    {
        var handler = new StubHandler(_ => JsonResponse("""
            {
              "model": "claude-3-5-sonnet",
              "content": [{ "type": "text", "text": "Claude's take." }],
              "usage": { "input_tokens": 90, "output_tokens": 30 }
            }
            """));
        var client = BuildClient(new LlmOptions
        {
            Enabled = true,
            Provider = LlmProvider.Anthropic,
            ApiKey = "anth-key",
            Model = "claude-3-5-sonnet"
        }, handler);

        var completion = await client.CompleteAsync(Req("claude-3-5-sonnet"));

        Assert.Equal("https://api.anthropic.com/v1/messages", handler.LastRequest?.RequestUri?.AbsoluteUri);
        Assert.Equal("anth-key", handler.LastRequest?.Headers.GetValues("x-api-key").Single());
        Assert.Equal("2023-06-01", handler.LastRequest?.Headers.GetValues("anthropic-version").Single());
        Assert.Equal("Claude's take.", completion.Text);
        Assert.Equal(90, completion.PromptTokens);
        Assert.Equal(30, completion.CompletionTokens);

        using var body = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal(512, body.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.Equal("system prompt", body.RootElement.GetProperty("system").GetString());
    }

    // -------------------------------------------------------------------------
    // Ollama transport
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Ollama_UsesLocalDefaultBaseUrl_AndOmitsAuthHeaderWhenNoKey()
    {
        var handler = new StubHandler(_ => JsonResponse(
            """{ "choices": [{ "message": { "content": "local answer" } }], "usage": { "prompt_tokens": 5, "completion_tokens": 3 } }"""));
        var client = BuildClient(new LlmOptions
        {
            Enabled = true,
            Provider = LlmProvider.Ollama,
            Model = "llama3.1"
        }, handler);

        var completion = await client.CompleteAsync(Req("llama3.1"));

        Assert.Equal("http://localhost:11434/v1/chat/completions", handler.LastRequest?.RequestUri?.AbsoluteUri);
        Assert.Null(handler.LastRequest?.Headers.Authorization);
        Assert.Equal("local answer", completion.Text);
    }

    [Fact]
    public async Task Ollama_UsesConfiguredBaseUrl()
    {
        var handler = new StubHandler(_ => JsonResponse("""{ "choices": [{ "message": { "content": "x" } }] }"""));
        var client = BuildClient(new LlmOptions
        {
            Enabled = true,
            Provider = LlmProvider.Ollama,
            BaseUrl = "http://10.0.0.5:1234/",
            Model = "qwen"
        }, handler);

        await client.CompleteAsync(Req("qwen"));

        Assert.Equal("http://10.0.0.5:1234/v1/chat/completions", handler.LastRequest?.RequestUri?.AbsoluteUri);
    }

    // -------------------------------------------------------------------------
    // OpenRouter transport
    // -------------------------------------------------------------------------

    [Fact]
    public async Task OpenRouter_SendsRefererHeaders_ToItsOpenAiCompatibleEndpoint()
    {
        var handler = new StubHandler(_ => JsonResponse("""{ "choices": [{ "message": { "content": "routed" } }] }"""));
        var client = BuildClient(new LlmOptions
        {
            Enabled = true,
            Provider = LlmProvider.OpenRouter,
            ApiKey = "or-key",
            Model = "gpt-4o-mini"
        }, handler);

        await client.CompleteAsync(Req());

        Assert.Equal("https://openrouter.ai/api/v1/chat/completions", handler.LastRequest?.RequestUri?.AbsoluteUri);
        Assert.Equal("Bearer or-key", handler.LastRequest?.Headers.Authorization?.ToString());
        Assert.Contains(handler.LastRequest!.Headers.GetValues("HTTP-Referer"), v => v.Contains("github.com"));
        Assert.Contains(handler.LastRequest!.Headers.GetValues("X-Title"), v => v.Contains("shortnr"));
    }

    // -------------------------------------------------------------------------
    // Error mapping and graceful degradation
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, LlmErrorKind.Auth)]
    [InlineData(HttpStatusCode.Forbidden, LlmErrorKind.Auth)]
    [InlineData(HttpStatusCode.TooManyRequests, LlmErrorKind.RateLimit)]
    [InlineData(HttpStatusCode.PaymentRequired, LlmErrorKind.PaymentRequired)]
    [InlineData(HttpStatusCode.InternalServerError, LlmErrorKind.Upstream)]
    public async Task NonSuccessStatus_MapsToErrorKind(HttpStatusCode status, LlmErrorKind expected)
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent("""{ "error": { "message": "nope" } }""", Encoding.UTF8, "application/json")
        });
        var client = BuildClient(OpenAiOptions(), handler);

        var ex = await Assert.ThrowsAsync<LlmException>(() => client.CompleteAsync(Req()));

        Assert.Equal(expected, ex.Kind);
    }

    [Fact]
    public async Task NetworkFailure_ThrowsNetworkError()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("connection refused"));
        var client = BuildClient(OpenAiOptions(), handler);

        var ex = await Assert.ThrowsAsync<LlmException>(() => client.CompleteAsync(Req()));

        Assert.Equal(LlmErrorKind.Network, ex.Kind);
    }

    [Fact]
    public async Task Timeout_ThrowsTimeoutError()
    {
        var handler = new StubHandler(_ => throw new TaskCanceledException("timed out"));
        var client = BuildClient(OpenAiOptions(), handler);

        var ex = await Assert.ThrowsAsync<LlmException>(() => client.CompleteAsync(Req()));

        Assert.Equal(LlmErrorKind.Timeout, ex.Kind);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> onSend) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return onSend(request);
        }
    }
}