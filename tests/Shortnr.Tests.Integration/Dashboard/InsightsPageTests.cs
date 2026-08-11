using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Tests.Integration.Infrastructure;

namespace Shortnr.Tests.Integration.Dashboard;

/// <summary>
/// Verifies the /insights page (PRD-006): 404 when the feature is disabled,
/// auth enforcement, owner-scoped listing, and accept/reject actions.
/// </summary>
public class InsightsPageTests : IAsyncLifetime
{
    private static readonly Regex AntiforgeryTokenRegex = new(
        @"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""",
        RegexOptions.Compiled);

    private readonly ShortnrWebAppFactory _disabled = new(authEnabled: false, aiInsightsEnabled: false);
    private readonly ShortnrWebAppFactory _enabled = new(authEnabled: true, aiInsightsEnabled: true);

    private User _alice = null!;
    private User _bob = null!;
    private User _carol = null!;
    private long _aliceSuggestionId;
    private long _bobSuggestionId;

    public async Task InitializeAsync()
    {
        using (var scope = _enabled.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync();

            _alice = new User { Issuer = ShortnrWebAppFactory.TestIssuer, Subject = "alice", Email = "alice@example.com", CreatedAtUtc = DateTime.UtcNow, LastLoginAtUtc = DateTime.UtcNow };
            _bob = new User { Issuer = ShortnrWebAppFactory.TestIssuer, Subject = "bob", Email = "bob@example.com", CreatedAtUtc = DateTime.UtcNow, LastLoginAtUtc = DateTime.UtcNow };
            _carol = new User { Issuer = ShortnrWebAppFactory.TestIssuer, Subject = "carol", Email = "carol@example.com", CreatedAtUtc = DateTime.UtcNow, LastLoginAtUtc = DateTime.UtcNow };
            db.Users.AddRange(_alice, _bob, _carol);
            await db.SaveChangesAsync();

            var aliceLink = new ShortenedUrl { LongUrl = "https://example.com/a", ShortCode = "aaa111", OwnerUserId = _alice.Id, ClickCount = 20, CreatedAtUtc = DateTime.UtcNow };
            var bobLink = new ShortenedUrl { LongUrl = "https://example.com/b", ShortCode = "bbb111", OwnerUserId = _bob.Id, ClickCount = 20, CreatedAtUtc = DateTime.UtcNow };
            db.ShortenedUrls.AddRange(aliceLink, bobLink);
            await db.SaveChangesAsync();

            var aliceSuggestion = new TagSuggestion
            {
                ShortenedUrlId = aliceLink.Id,
                SuggestedTag = "facebook.com",
                Source = TagSuggestionSource.ReferrerDomainCluster,
                ClickCount = 12,
                FirstObservedUtc = DateTime.UtcNow,
                Status = TagSuggestionStatus.Pending,
                CreatedAtUtc = DateTime.UtcNow
            };
            var bobSuggestion = new TagSuggestion
            {
                ShortenedUrlId = bobLink.Id,
                SuggestedTag = "newsletter",
                Source = TagSuggestionSource.UtmExtraction,
                ClickCount = 0,
                FirstObservedUtc = DateTime.UtcNow,
                Status = TagSuggestionStatus.Pending,
                CreatedAtUtc = DateTime.UtcNow
            };
            db.TagSuggestions.AddRange(aliceSuggestion, bobSuggestion);
            await db.SaveChangesAsync();

            _aliceSuggestionId = aliceSuggestion.Id;
            _bobSuggestionId = bobSuggestion.Id;
        }

        using (var scope = _disabled.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync();
        }
    }

    public async Task DisposeAsync()
    {
        await _disabled.DisposeAsync();
        await _enabled.DisposeAsync();
    }

    private HttpClient AuthenticatedClient()
    {
        var authState = _enabled.Services.GetRequiredService<TestAuthState>();
        authState.SetAuthenticatedUser("alice", ShortnrWebAppFactory.TestIssuer);
        return _enabled.CreateClientNoRedirect();
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
    // Feature gate
    // -------------------------------------------------------------------------

    [Fact]
    public async Task WhenDisabled_Returns404()
    {
        var client = _disabled.CreateClientNoRedirect();

        var response = await client.GetAsync("/insights");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Auth enforcement (feature enabled)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task WhenEnabled_UnauthenticatedFullPage_RedirectsToIndex()
    {
        var client = _enabled.CreateClientNoRedirect();

        var response = await client.GetAsync("/insights");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task WhenEnabled_UnauthenticatedHtmx_ReturnsUnauthorized()
    {
        var client = _enabled.CreateClientNoRedirect();
        var request = new HttpRequestMessage(HttpMethod.Get, "/insights");
        request.Headers.Add("HX-Request", "true");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Listing and scoping
    // -------------------------------------------------------------------------

    [Fact]
    public async Task WhenEnabled_Authenticated_ShowsOwnPendingSuggestions()
    {
        var client = AuthenticatedClient();

        var response = await client.GetAsync("/insights");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("facebook.com", html);
        Assert.Contains("aaa111", html);
    }

    [Fact]
    public async Task WhenEnabled_SuggestionsAreScopedToOwner()
    {
        var client = AuthenticatedClient();

        var response = await client.GetAsync("/insights");
        var html = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("newsletter", html);
        Assert.DoesNotContain("bbb111", html);
    }

    [Fact]
    public async Task WhenEnabled_NoSuggestions_ShowsEmptyState()
    {
        var authState = _enabled.Services.GetRequiredService<TestAuthState>();
        authState.SetAuthenticatedUser("carol", ShortnrWebAppFactory.TestIssuer);
        var client = _enabled.CreateClientNoRedirect();

        var response = await client.GetAsync("/insights");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("No tag suggestions yet", html);
    }

    // -------------------------------------------------------------------------
    // Accept / dismiss actions
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Accept_AppliesTagAndRemovesFromList()
    {
        var client = AuthenticatedClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/insights?handler=accept", token,
            ("id", _aliceSuggestionId.ToString()));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("applied", html);
        Assert.DoesNotContain("aaa111", html);

        using var scope = _enabled.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var suggestion = await db.TagSuggestions.SingleAsync(s => s.Id == _aliceSuggestionId);
        Assert.Equal(TagSuggestionStatus.Accepted, suggestion.Status);
        Assert.Contains(await db.ShortenedUrlTags.ToListAsync(),
            t => t.Name == "facebook.com" && t.ShortenedUrlId == suggestion.ShortenedUrlId);
    }

    [Fact]
    public async Task Accept_ScopedToOwner_CannotAcceptAnotherOwnersSuggestion()
    {
        var client = AuthenticatedClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/insights?handler=accept", token,
            ("id", _bobSuggestionId.ToString()));

        Assert.Contains("not found", await response.Content.ReadAsStringAsync());

        using var scope = _enabled.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var suggestion = await db.TagSuggestions.SingleAsync(s => s.Id == _bobSuggestionId);
        Assert.Equal(TagSuggestionStatus.Pending, suggestion.Status);
        Assert.Empty(await db.ShortenedUrlTags.ToListAsync());
    }

    [Fact]
    public async Task Dismiss_MarksDismissedAndAppliesNoTag()
    {
        var client = AuthenticatedClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/insights?handler=dismiss", token,
            ("id", _aliceSuggestionId.ToString()));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("dismissed", html);
        Assert.DoesNotContain("aaa111", html);

        using var scope = _enabled.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var suggestion = await db.TagSuggestions.SingleAsync(s => s.Id == _aliceSuggestionId);
        Assert.Equal(TagSuggestionStatus.Dismissed, suggestion.Status);
        Assert.Empty(await db.ShortenedUrlTags.ToListAsync());
    }

    // -------------------------------------------------------------------------
    // Manual "Run analysis now" trigger
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunNow_TriggersAnalysisAndReturnsUpdatedList()
    {
        var client = AuthenticatedClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/insights?handler=RunNow", token);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Analysis complete", html);
        // The list is re-rendered from the DB in the same response, so alice's
        // still-pending suggestion should still be there alongside the status banner.
        Assert.Contains("aaa111", html);
    }
}
