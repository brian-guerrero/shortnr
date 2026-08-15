using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Web.Features.Insights.Llm;
using Shortnr.Tests.Integration.Infrastructure;

namespace Shortnr.Tests.Integration.Dashboard;

/// <summary>
/// Verifies the PRD-023 LLM analysis operations on the /insights page: the four
/// endpoints (analyze, optimize-campaign, draft-social-copy, suggest-tags), owner
/// scoping, auth enforcement, graceful degradation when the provider fails, cost
/// tracking into LlmUsageLog, and monthly-budget enforcement. The LLM transport is
/// stubbed with <see cref="FakeChatClient"/> via ConfigureTestServices, so no real
/// provider API is ever contacted.
/// </summary>
public class LlmInsightsPageTests : IAsyncLifetime
{
    private readonly StubLlmFactory _factory = new(() => new FakeChatClient
    {
        Result = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Traffic spiked because of a Reddit post in r/selfhosted."))
        {
            ModelId = "gpt-4o-mini",
            Usage = new UsageDetails { InputTokenCount = 100, OutputTokenCount = 20 }
        }
    });

    private User _alice = null!;
    private User _bob = null!;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();

        _alice = new User { Issuer = ShortnrWebAppFactory.TestIssuer, Subject = "alice", Email = "alice@example.com", CreatedAtUtc = DateTime.UtcNow, LastLoginAtUtc = DateTime.UtcNow };
        _bob = new User { Issuer = ShortnrWebAppFactory.TestIssuer, Subject = "bob", Email = "bob@example.com", CreatedAtUtc = DateTime.UtcNow, LastLoginAtUtc = DateTime.UtcNow };
        db.Users.AddRange(_alice, _bob);
        await db.SaveChangesAsync();

        var aliceLink = new ShortenedUrl { LongUrl = "https://example.com/summer", ShortCode = "aaa111", OwnerUserId = _alice.Id, ClickCount = 40, CreatedAtUtc = DateTime.UtcNow };
        var bobLink = new ShortenedUrl { LongUrl = "https://example.com/bob", ShortCode = "bbb222", OwnerUserId = _bob.Id, ClickCount = 5, CreatedAtUtc = DateTime.UtcNow };
        db.ShortenedUrls.AddRange(aliceLink, bobLink);
        await db.SaveChangesAsync();

        db.ShortenedUrlTags.Add(new ShortenedUrlTag { ShortenedUrlId = aliceLink.Id, Name = "campaign-summer", CreatedAtUtc = DateTime.UtcNow });
        db.ClickEvents.AddRange(
            new ClickEvent { ShortenedUrlId = aliceLink.Id, Referer = "https://www.reddit.com/r/selfhosted", ClickedAtUtc = DateTime.UtcNow.AddDays(-1), UserAgent = "test", IpAddress = "1.1.1.1" },
            new ClickEvent { ShortenedUrlId = aliceLink.Id, Referer = "https://www.reddit.com/r/selfhosted", ClickedAtUtc = DateTime.UtcNow.AddHours(-2), UserAgent = "test", IpAddress = "1.1.1.2" });
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private HttpClient AuthenticatedClient(ShortnrWebAppFactory factory)
    {
        factory.Services.GetRequiredService<TestAuthState>().SetAuthenticatedUser("alice", ShortnrWebAppFactory.TestIssuer);
        return factory.CreateClientNoRedirect();
    }

    private HttpClient AuthenticatedClient() => AuthenticatedClient(_factory);

    /// <summary>Seeds an alice user + link so link-scoped analysis endpoints find data.</summary>
    private static async Task SeedAliceLinkAsync(ShortnrWebAppFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();

        var alice = new User { Issuer = ShortnrWebAppFactory.TestIssuer, Subject = "alice", Email = "alice@example.com", CreatedAtUtc = DateTime.UtcNow, LastLoginAtUtc = DateTime.UtcNow };
        db.Users.Add(alice);
        await db.SaveChangesAsync();

        db.ShortenedUrls.Add(new ShortenedUrl { LongUrl = "https://example.com/summer", ShortCode = "aaa111", OwnerUserId = alice.Id, ClickCount = 40, CreatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        var page = await client.GetAsync("/insights");
        var html = await page.Content.ReadAsStringAsync();
        var match = AntiforgeryTokenRegex.Match(html);
        Assert.True(match.Success, "Antiforgery token not found in insights page.");
        return match.Groups[1].Value;
    }

    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string path, string token, params (string Name, string Value)[] fields)
    {
        var pairs = fields
            .Select(f => new KeyValuePair<string, string>(f.Name, f.Value))
            .ToList();
        pairs.Add(new KeyValuePair<string, string>("__RequestVerificationToken", token));
        return await client.PostAsync(path, new FormUrlEncodedContent(pairs));
    }

    // -------------------------------------------------------------------------
    // Feature gating (AiInsights on, LLM off)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task WhenLlmDisabled_Page_HidesAskAiSection()
    {
        using var factory = new ShortnrWebAppFactory(authEnabled: false, aiInsightsEnabled: true, llmEnabled: false);
        var client = factory.CreateClientNoRedirect();

        var html = await (await client.GetAsync("/insights")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("Ask AI", html);
    }

    [Fact]
    public async Task WhenLlmDisabled_Analyze_ShowsDisabledMessageWithoutTouchingProvider()
    {
        using var factory = new ShortnrWebAppFactory(authEnabled: true, aiInsightsEnabled: true, llmEnabled: false);
        await SeedAliceLinkAsync(factory);
        var client = AuthenticatedClient(factory);
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/insights?handler=Analyze", token, ("code", "aaa111"));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("not enabled", html);
        Assert.DoesNotContain("AI-generated", html);
    }

    // -------------------------------------------------------------------------
    // Auth gating and UI surfacing (LLM on)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task WhenLlmEnabled_UnauthenticatedHtmxGet_ReturnsUnauthorized()
    {
        var client = _factory.CreateClientNoRedirect();
        var request = new HttpRequestMessage(HttpMethod.Get, "/insights");
        request.Headers.Add("HX-Request", "true");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WhenLlmEnabled_AuthenticatedGet_ShowsAskAiSection()
    {
        var client = AuthenticatedClient();

        var response = await client.GetAsync("/insights");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Ask AI", html);
        Assert.Contains("verify before publishing", html);
    }

    // -------------------------------------------------------------------------
    // Each analysis endpoint (happy path)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Analyze_ReturnsGeneratedContentAndDisclaimer()
    {
        var client = AuthenticatedClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/insights?handler=Analyze", token, ("code", "aaa111"));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Traffic spiked because of a Reddit post", html);
        Assert.Contains("verify before publishing", html);
    }

    [Fact]
    public async Task Analyze_UnknownCode_ShowsNotFound()
    {
        var client = AuthenticatedClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/insights?handler=Analyze", token, ("code", "zzz999"));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("No link with code", html);
        Assert.DoesNotContain("AI-generated", html);
    }

    [Fact]
    public async Task Analyze_AnotherOwnersLink_IsScopedOut()
    {
        var client = AuthenticatedClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/insights?handler=Analyze", token, ("code", "bbb222"));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("No link with code", html);
        Assert.DoesNotContain("AI-generated", html);
    }

    [Fact]
    public async Task OptimizeCampaign_ReturnsSuggestions()
    {
        var client = AuthenticatedClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/insights?handler=OptimizeCampaign", token, ("tag", "campaign-summer"));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Traffic spiked because of a Reddit post", html);
    }

    [Fact]
    public async Task OptimizeCampaign_UnknownTag_ShowsNotFound()
    {
        var client = AuthenticatedClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/insights?handler=OptimizeCampaign", token, ("tag", "no-such-campaign"));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("No links tagged", html);
    }

    [Fact]
    public async Task DraftSocialCopy_ReturnsCopy()
    {
        var client = AuthenticatedClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/insights?handler=DraftSocialCopy", token, ("code", "aaa111"));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Traffic spiked because of a Reddit post", html);
        Assert.Contains("verify before publishing", html);
    }

    [Fact]
    public async Task SuggestTags_ReturnsTags()
    {
        var client = AuthenticatedClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/insights?handler=SuggestTags", token, ("url", "https://example.com/guide"));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Traffic spiked because of a Reddit post", html);
    }

    [Fact]
    public async Task SuggestTags_InvalidUrl_ShowsError()
    {
        var client = AuthenticatedClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/insights?handler=SuggestTags", token, ("url", "not a url"));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("full URL", html);
    }

    // -------------------------------------------------------------------------
    // Graceful degradation (stubbed provider failures)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProviderRateLimited_ShowsFriendlyError_AndRecordsFailedUsage()
    {
        using var factory = MakeStubFactory(new FakeChatClient { Throw = new LlmException("rate limited", LlmErrorKind.RateLimit) });
        var client = AuthenticatedClient(factory);
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/insights?handler=SuggestTags", token, ("url", "https://example.com/x"));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("rate-limited", html);
        Assert.DoesNotContain("Shortnr.Web.Features", html);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = Assert.Single(await db.LlmUsageLogs.ToListAsync());
        Assert.False(row.Success);
        Assert.Equal("RateLimit", row.ErrorKind);
        Assert.Equal("SuggestTags", row.Operation);
    }

    [Fact]
    public async Task ProviderGenericFailure_ShowsFriendlyError()
    {
        using var factory = MakeStubFactory(new FakeChatClient { Throw = new InvalidOperationException("provider exploded") });
        var client = AuthenticatedClient(factory);
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/insights?handler=SuggestTags", token, ("url", "https://example.com/x"));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Something went wrong", html);
        Assert.DoesNotContain("provider exploded", html);
    }

    // -------------------------------------------------------------------------
    // Cost tracking
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SuccessfulCall_RecordsUsageRow()
    {
        var client = AuthenticatedClient();
        var token = await GetAntiforgeryTokenAsync(client);

        await PostFormAsync(client, "/insights?handler=Analyze", token, ("code", "aaa111"));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = Assert.Single(await db.LlmUsageLogs.ToListAsync());
        Assert.True(row.Success);
        Assert.Equal("gpt-4o-mini", row.Model);
        Assert.Equal("AnalyzeTraffic", row.Operation);
        Assert.Equal(100, row.PromptTokens);
        Assert.Equal(20, row.CompletionTokens);
        Assert.True(row.EstimatedCostUsd > 0);
        Assert.Equal(_alice.Id, row.OwnerUserId);
    }

    // -------------------------------------------------------------------------
    // Monthly budget
    // -------------------------------------------------------------------------

    [Fact]
    public async Task BudgetExceeded_BlocksCallWithoutInvokingProvider()
    {
        var client = new FakeChatClient();
        using var factory = MakeBudgetFactory(client, monthlyBudget: 0.0001m);
        var factoryClient = AuthenticatedClient(factory);
        var token = await GetAntiforgeryTokenAsync(factoryClient);

        var response = await PostFormAsync(factoryClient, "/insights?handler=SuggestTags", token, ("url", "https://example.com/x"));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("budget has been reached", html);
        Assert.Equal(0, client.Calls);
    }

    [Fact]
    public async Task WithinBudget_AllowsCall()
    {
        var client = new FakeChatClient();
        using var factory = MakeBudgetFactory(client, monthlyBudget: 100m);
        var factoryClient = AuthenticatedClient(factory);
        var token = await GetAntiforgeryTokenAsync(factoryClient);

        var response = await PostFormAsync(factoryClient, "/insights?handler=SuggestTags", token, ("url", "https://example.com/x"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, client.Calls);
    }

    // -------------------------------------------------------------------------
    // Test infrastructure
    // -------------------------------------------------------------------------

    private static readonly Regex AntiforgeryTokenRegex = new(
        @"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""",
        RegexOptions.Compiled);

    private static StubLlmFactory MakeStubFactory(IChatClient stub) => new(() => stub);

    private static StubLlmFactory MakeBudgetFactory(IChatClient stub, decimal monthlyBudget) =>
        new(() => stub, monthlyBudget: monthlyBudget);

    private sealed class StubLlmFactory(Func<IChatClient> clientFactory, decimal? monthlyBudget = null, bool authEnabled = true)
        : ShortnrWebAppFactory(authEnabled, aiInsightsEnabled: true, llmEnabled: true)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            if (monthlyBudget is not null)
            {
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        // Make the built-in price table effectively $1/token so any
                        // prompt estimate dwarfs the tiny budget.
                        ["AiInsights:Llm:MonthlyBudget"] = monthlyBudget.Value.ToString(CultureInfo.InvariantCulture),
                        ["AiInsights:Llm:InputPricePerMillion"] = "1000000",
                        ["AiInsights:Llm:OutputPricePerMillion"] = "1000000"
                    });
                });
            }

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IChatClient>();
                services.AddSingleton(clientFactory());
            });
        }
    }

    private sealed class FakeChatClient : IChatClient
    {
        public int Calls { get; private set; }
        public ChatResponse Result { get; set; } = new(new ChatMessage(ChatRole.Assistant, "ok"))
        {
            ModelId = "gpt-4o-mini",
            Usage = new UsageDetails { InputTokenCount = 10, OutputTokenCount = 5 }
        };
        public Exception? Throw { get; set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Throw is not null) throw Throw;
            return Task.FromResult(Result);
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}