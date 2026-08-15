using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Shortnr.Web.Features.Insights.Llm;

namespace Shortnr.Tests.Unit.Services;

/// <summary>
/// Covers the one remaining hand-written provider transport (no first-party
/// Microsoft.Extensions.AI/Aspire package exists for Anthropic). OpenAI/OpenRouter/Ollama wire
/// formats are no longer tested here -- they're owned by the OpenAI SDK and OllamaSharp
/// packages now, not by this codebase.
/// </summary>
public class AnthropicChatClientTests
{
    private static AnthropicChatClient BuildClient(StubHandler handler, string apiKey = "anth-key", string? baseUrl = null)
    {
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        return new AnthropicChatClient(http, apiKey, baseUrl);
    }

    private static ChatMessage[] Messages() =>
    [
        new(ChatRole.System, "system prompt"),
        new(ChatRole.User, "user prompt")
    ];

    [Fact]
    public async Task CallsV1Messages_WithVersionHeaders_AndParsesResponse()
    {
        var handler = new StubHandler(_ => JsonResponse("""
            {
              "model": "claude-3-5-sonnet",
              "content": [{ "type": "text", "text": "Claude's take." }],
              "usage": { "input_tokens": 90, "output_tokens": 30 }
            }
            """));
        var client = BuildClient(handler);

        var response = await client.GetResponseAsync(Messages(), new ChatOptions { ModelId = "claude-3-5-sonnet", MaxOutputTokens = 512 });

        Assert.Equal("https://api.anthropic.com/v1/messages", handler.LastRequest?.RequestUri?.AbsoluteUri);
        Assert.Equal("anth-key", handler.LastRequest?.Headers.GetValues("x-api-key").Single());
        Assert.Equal("2023-06-01", handler.LastRequest?.Headers.GetValues("anthropic-version").Single());
        Assert.Equal("Claude's take.", response.Text);
        Assert.Equal(90, response.Usage?.InputTokenCount);
        Assert.Equal(30, response.Usage?.OutputTokenCount);
        Assert.Equal("claude-3-5-sonnet", response.ModelId);

        using var body = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal(512, body.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.Equal("system prompt", body.RootElement.GetProperty("system").GetString());
    }

    [Fact]
    public async Task UsesConfiguredBaseUrl()
    {
        var handler = new StubHandler(_ => JsonResponse("""{ "content": [{ "type": "text", "text": "x" }] }"""));
        var client = BuildClient(handler, baseUrl: "http://10.0.0.5:1234/");

        await client.GetResponseAsync(Messages(), new ChatOptions { ModelId = "claude-3-5-sonnet" });

        Assert.Equal("http://10.0.0.5:1234/v1/messages", handler.LastRequest?.RequestUri?.AbsoluteUri);
    }

    [Fact]
    public async Task WhenUsageMissing_DefaultsTokensToZero()
    {
        var handler = new StubHandler(_ => JsonResponse("""{ "content": [{ "type": "text", "text": "ok" }] }"""));
        var client = BuildClient(handler);

        var response = await client.GetResponseAsync(Messages(), new ChatOptions { ModelId = "claude-3-5-sonnet" });

        Assert.Equal(0, response.Usage?.InputTokenCount);
        Assert.Equal(0, response.Usage?.OutputTokenCount);
    }

    [Fact]
    public async Task NonSuccessStatus_ThrowsAnthropicHttpException_WithStatusAndMessage()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("""{ "error": { "message": "slow down" } }""", Encoding.UTF8, "application/json")
        });
        var client = BuildClient(handler);

        var ex = await Assert.ThrowsAsync<AnthropicHttpException>(
            () => client.GetResponseAsync(Messages(), new ChatOptions { ModelId = "claude-3-5-sonnet" }));

        Assert.Equal(HttpStatusCode.TooManyRequests, ex.StatusCode);
        Assert.Equal("slow down", ex.Message);
    }

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
