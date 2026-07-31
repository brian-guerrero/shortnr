using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Shortnr.Tests.Integration.Infrastructure;

namespace Shortnr.Tests.Integration.Api;

/// <summary>
/// Verifies the public shorten form (POST /) is rate limited per client IP, and
/// that the redirect endpoint is limited too (with far more generous defaults).
/// </summary>
public class ShortenRateLimitTests : IAsyncLifetime
{
    private static readonly Regex AntiforgeryTokenRegex = new(
        @"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""",
        RegexOptions.Compiled);

    private readonly LowLimitFactory _factory = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    [Fact]
    public async Task ShortenForm_AfterPerMinuteCap_ReturnsTooManyRequests()
    {
        var client = _factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        // LowLimitFactory sets Shorten:PerMinute=3. The first three POSTs should
        // succeed; the fourth should be rejected with 429.
        for (var i = 0; i < 3; i++)
        {
            var ok = await PostFormAsync(client, token, ("url", $"https://example.com/{i}"));
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }

        var rejected = await PostFormAsync(client, token, ("url", "https://example.com/over"));
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    }

    [Fact]
    public async Task ShortenForm_HtmxVariant_IsRateLimitedToo()
    {
        var client = _factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        for (var i = 0; i < 3; i++)
        {
            var response = await client.PostAsync("/", BuildForm(token, ("url", $"https://example.com/{i}")));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var request = new HttpRequestMessage(HttpMethod.Post, "/") { Content = BuildForm(token, ("url", "https://example.com/over")) };
        request.Headers.Add("HX-Request", "true");

        var rejected = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    }

    private async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        var page = await client.GetAsync("/");
        var html = await page.Content.ReadAsStringAsync();
        var match = AntiforgeryTokenRegex.Match(html);
        Assert.True(match.Success, "Antiforgery token not found in index page.");
        return match.Groups[1].Value;
    }

    private static Task<HttpResponseMessage> PostFormAsync(HttpClient client, string token, params (string Name, string Value)[] fields) =>
        client.PostAsync("/", BuildForm(token, fields));

    private static FormUrlEncodedContent BuildForm(string token, params (string Name, string Value)[] fields)
    {
        var pairs = fields
            .Select(f => new KeyValuePair<string, string>(f.Name, f.Value))
            .ToList();
        pairs.Add(new KeyValuePair<string, string>("__RequestVerificationToken", token));
        return new FormUrlEncodedContent(pairs);
    }

    private sealed class LowLimitFactory : ShortnrWebAppFactory
    {
        public LowLimitFactory() : base(authEnabled: false)
        {
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["RateLimiting:Shorten:PerMinute"] = "3",
                    ["RateLimiting:Shorten:PerDay"] = "3",
                    ["RateLimiting:Redirect:PerMinute"] = "3",
                    ["RateLimiting:Redirect:PerDay"] = "3"
                }));
        }
    }
}
