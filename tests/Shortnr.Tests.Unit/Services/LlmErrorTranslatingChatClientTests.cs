using System.Net;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Shortnr.Web.Features.Insights.Llm;

namespace Shortnr.Tests.Unit.Services;

/// <summary>
/// Covers <see cref="LlmErrorTranslatingChatClient"/>'s exception-to-<see cref="LlmErrorKind"/>
/// mapping -- the direct replacement for the old <c>LlmClient.BuildHttpError</c> switch, now
/// re-triggered from whatever exception type each provider's SDK throws instead of a raw
/// <see cref="HttpResponseMessage"/>. Full HTTP-status coverage is exercised once via the
/// (trivially-constructible) <see cref="AnthropicHttpException"/> path -- the mapping itself
/// is shared across every catch clause, so it doesn't need re-proving per exception type.
/// </summary>
public class LlmErrorTranslatingChatClientTests
{
    private static LlmErrorTranslatingChatClient Build(FakeInnerChatClient inner) =>
        new(inner, NullLogger<LlmErrorTranslatingChatClient>.Instance);

    private static ChatMessage[] Messages() => [new(ChatRole.User, "hi")];

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, LlmErrorKind.Auth)]
    [InlineData(HttpStatusCode.Forbidden, LlmErrorKind.Auth)]
    [InlineData(HttpStatusCode.TooManyRequests, LlmErrorKind.RateLimit)]
    [InlineData(HttpStatusCode.PaymentRequired, LlmErrorKind.PaymentRequired)]
    [InlineData(HttpStatusCode.BadRequest, LlmErrorKind.BadRequest)]
    [InlineData(HttpStatusCode.UnprocessableEntity, LlmErrorKind.BadRequest)]
    [InlineData(HttpStatusCode.RequestTimeout, LlmErrorKind.Timeout)]
    [InlineData(HttpStatusCode.GatewayTimeout, LlmErrorKind.Timeout)]
    [InlineData(HttpStatusCode.InternalServerError, LlmErrorKind.Upstream)]
    public async Task AnthropicHttpException_MapsToErrorKind(HttpStatusCode status, LlmErrorKind expected)
    {
        var client = Build(new FakeInnerChatClient { Throw = new AnthropicHttpException(status, "nope") });

        var ex = await Assert.ThrowsAsync<LlmException>(() => client.GetResponseAsync(Messages()));

        Assert.Equal(expected, ex.Kind);
    }

    [Fact]
    public async Task HttpRequestException_WithoutStatusCode_MapsToNetwork()
    {
        var client = Build(new FakeInnerChatClient { Throw = new HttpRequestException("connection refused") });

        var ex = await Assert.ThrowsAsync<LlmException>(() => client.GetResponseAsync(Messages()));

        Assert.Equal(LlmErrorKind.Network, ex.Kind);
    }

    [Fact]
    public async Task HttpRequestException_WithStatusCode_MapsViaStatus()
    {
        var client = Build(new FakeInnerChatClient
        {
            Throw = new HttpRequestException("rate limited", null, HttpStatusCode.TooManyRequests)
        });

        var ex = await Assert.ThrowsAsync<LlmException>(() => client.GetResponseAsync(Messages()));

        Assert.Equal(LlmErrorKind.RateLimit, ex.Kind);
    }

    [Fact]
    public async Task OperationCanceled_WithoutCallerCancellation_MapsToTimeout()
    {
        var client = Build(new FakeInnerChatClient { Throw = new OperationCanceledException("timed out") });

        var ex = await Assert.ThrowsAsync<LlmException>(() => client.GetResponseAsync(Messages(), cancellationToken: CancellationToken.None));

        Assert.Equal(LlmErrorKind.Timeout, ex.Kind);
    }

    private sealed class FakeInnerChatClient : IChatClient
    {
        public Exception? Throw { get; set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Throw is not null ? throw Throw : Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
