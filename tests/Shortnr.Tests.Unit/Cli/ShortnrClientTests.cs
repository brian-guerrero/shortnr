using System.Net;
using System.Text.Json;
using Shortnr.Cli.Models;
using Shortnr.Cli.Services;

namespace Shortnr.Tests.Unit.Cli;

public class ShortnrClientTests
{
    [Fact]
    public async Task CreateLinkAsync_WithSuccess_ReturnsLink()
    {
        var expectedLink = new LinkResponse("abc123", "http://localhost/abc123", "https://example.com", null, 0, DateTime.UtcNow);
        var handler = new MockHttpHandler(HttpStatusCode.Created, JsonSerializer.Serialize(expectedLink, ApiJsonContext.Default.LinkResponse));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var client = new ShortnrClient(http, "snr_test");

        var result = await client.CreateLinkAsync("https://example.com", null, null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("abc123", result.Value!.ShortCode);
        Assert.Equal("https://example.com", result.Value.LongUrl);
    }

    [Fact]
    public async Task CreateLinkAsync_WithValidationError_ReturnsFailure()
    {
        var error = new ErrorResponse("validation", "Validation failed", 400, new Dictionary<string, string[]> { ["url"] = ["Invalid URL"] });
        var handler = new MockHttpHandler(HttpStatusCode.BadRequest, JsonSerializer.Serialize(error, ApiJsonContext.Default.ErrorResponse));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var client = new ShortnrClient(http, "snr_test");

        var result = await client.CreateLinkAsync("invalid", null, null, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("Invalid URL", result.Error);
    }

    [Fact]
    public async Task ListLinksAsync_WithSuccess_ReturnsList()
    {
        var expectedList = new LinkListResponse(
            [new LinkResponse("abc", "http://localhost/abc", "https://example.com", null, 5, DateTime.UtcNow)],
            1, 20, 1);
        var handler = new MockHttpHandler(HttpStatusCode.OK, JsonSerializer.Serialize(expectedList, ApiJsonContext.Default.LinkListResponse));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var client = new ShortnrClient(http, "snr_test");

        var result = await client.ListLinksAsync(null, null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Links);
        Assert.Equal("abc", result.Value.Links[0].ShortCode);
    }

    [Fact]
    public async Task GetLinkAsync_WithSuccess_ReturnsLink()
    {
        var expectedLink = new LinkResponse("abc123", "http://localhost/abc123", "https://example.com", null, 10, DateTime.UtcNow);
        var handler = new MockHttpHandler(HttpStatusCode.OK, JsonSerializer.Serialize(expectedLink, ApiJsonContext.Default.LinkResponse));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var client = new ShortnrClient(http, "snr_test");

        var result = await client.GetLinkAsync("abc123", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("abc123", result.Value!.ShortCode);
        Assert.Equal(10, result.Value.ClickCount);
    }

    [Fact]
    public async Task GetLinkAsync_WithNotFound_ReturnsFailure()
    {
        var handler = new MockHttpHandler(HttpStatusCode.NotFound, "");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var client = new ShortnrClient(http, "snr_test");

        var result = await client.GetLinkAsync("nonexistent", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("404", result.Error);
    }

    [Fact]
    public async Task GetClicksAsync_WithSuccess_ReturnsClicks()
    {
        var expectedClicks = new ClickListResponse(
            [new ClickRow(1, "abc", "US", "United States", "New York", "Chrome", "120", "Windows", "10", null, "1.2.3.4", "Desktop", DateTime.UtcNow)],
            1, 20, 1);
        var handler = new MockHttpHandler(HttpStatusCode.OK, JsonSerializer.Serialize(expectedClicks, ApiJsonContext.Default.ClickListResponse));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var client = new ShortnrClient(http, "snr_test");

        var result = await client.GetClicksAsync("abc", null, null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Clicks);
        Assert.Equal("US", result.Value.Clicks[0].CountryCode);
    }

    [Fact]
    public async Task DeleteLinkAsync_WithSuccess_ReturnsSuccess()
    {
        var handler = new MockHttpHandler(HttpStatusCode.NoContent, "");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var client = new ShortnrClient(http, "snr_test");

        var result = await client.DeleteLinkAsync("abc123", CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task DeleteLinkAsync_WithNotFound_ReturnsFailure()
    {
        var handler = new MockHttpHandler(HttpStatusCode.NotFound, "");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var client = new ShortnrClient(http, "snr_test");

        var result = await client.DeleteLinkAsync("nonexistent", CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    private class MockHttpHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _responseContent;

        public MockHttpHandler(HttpStatusCode statusCode, string responseContent)
        {
            _statusCode = statusCode;
            _responseContent = responseContent;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseContent, System.Text.Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}
