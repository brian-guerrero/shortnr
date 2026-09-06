using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Tests.Integration.Infrastructure;

namespace Shortnr.Tests.Integration.Settings;

public class WebhooksSettingsTests : IntegrationTestBase
{

    [Fact]
    public async Task Get_WhenAuthenticated_ReturnsWebhooksPage()
    {
        Factory.Services.GetRequiredService<TestAuthState>().SetAuthenticatedUser("alice", ShortnrWebAppFactory.TestIssuer);
        await SeedUserAsync("alice", ShortnrWebAppFactory.TestIssuer);

        var client = Factory.CreateClient();
        var response = await client.GetAsync("/settings/webhooks");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Webhooks", body);
        Assert.Contains("No webhooks yet", body);
    }

    [Fact]
    public async Task Post_WithValidData_CreatesWebhook()
    {
        Factory.Services.GetRequiredService<TestAuthState>().SetAuthenticatedUser("alice", ShortnrWebAppFactory.TestIssuer);
        await SeedUserAsync("alice", ShortnrWebAppFactory.TestIssuer);

        var client = Factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/settings/webhooks", token,
            ("url", "https://example.com/webhook"),
            ("event", WebhookEventTypes.LinkCreated),
            ("event", WebhookEventTypes.LinkClicked));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Webhook created", body);
        Assert.Contains("Copy this secret now", body);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var webhook = await db.Webhooks.SingleAsync();
        Assert.Equal("https://example.com/webhook", webhook.Url);
        Assert.Contains(WebhookEventTypes.LinkCreated, webhook.EventTypes);
        Assert.Contains(WebhookEventTypes.LinkClicked, webhook.EventTypes);
        Assert.True(webhook.IsActive);
    }

    [Fact]
    public async Task Post_WithPrivateIPUrl_ReturnsError()
    {
        Factory.Services.GetRequiredService<TestAuthState>().SetAuthenticatedUser("alice", ShortnrWebAppFactory.TestIssuer);
        await SeedUserAsync("alice", ShortnrWebAppFactory.TestIssuer);

        var client = Factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/settings/webhooks", token,
            ("url", "http://192.168.1.1/webhook"),
            ("event", WebhookEventTypes.LinkCreated));

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Private and internal IP addresses are not allowed", body);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await db.Webhooks.CountAsync());
    }

    [Fact]
    public async Task Post_WithLocalhostUrl_ReturnsError()
    {
        Factory.Services.GetRequiredService<TestAuthState>().SetAuthenticatedUser("alice", ShortnrWebAppFactory.TestIssuer);
        await SeedUserAsync("alice", ShortnrWebAppFactory.TestIssuer);

        var client = Factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/settings/webhooks", token,
            ("url", "http://localhost/webhook"),
            ("event", WebhookEventTypes.LinkCreated));

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("localhost is not allowed", body);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await db.Webhooks.CountAsync());
    }

    [Fact]
    public async Task Post_WithNoEvents_ReturnsError()
    {
        Factory.Services.GetRequiredService<TestAuthState>().SetAuthenticatedUser("alice", ShortnrWebAppFactory.TestIssuer);
        await SeedUserAsync("alice", ShortnrWebAppFactory.TestIssuer);

        var client = Factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/settings/webhooks", token,
            ("url", "https://example.com/webhook"));

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Select at least one valid event type", body);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await db.Webhooks.CountAsync());
    }

    [Fact]
    public async Task Post_WithNoUrl_ReturnsError()
    {
        Factory.Services.GetRequiredService<TestAuthState>().SetAuthenticatedUser("alice", ShortnrWebAppFactory.TestIssuer);
        await SeedUserAsync("alice", ShortnrWebAppFactory.TestIssuer);

        var client = Factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/settings/webhooks", token,
            ("event", WebhookEventTypes.LinkCreated));

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Enter a webhook URL", body);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await db.Webhooks.CountAsync());
    }

    [Fact]
    public async Task Revoke_DisablesWebhook()
    {
        Factory.Services.GetRequiredService<TestAuthState>().SetAuthenticatedUser("alice", ShortnrWebAppFactory.TestIssuer);
        await SeedUserAsync("alice", ShortnrWebAppFactory.TestIssuer);
        var webhookId = await SeedWebhookAsync("alice", ShortnrWebAppFactory.TestIssuer, "https://example.com/webhook");

        var client = Factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/settings/webhooks?handler=Revoke", token,
            ("id", webhookId.ToString()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("disabled", body);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var webhook = await db.Webhooks.SingleAsync(w => w.Id == webhookId);
        Assert.False(webhook.IsActive);
    }

    [Fact]
    public async Task Activate_ReEnablesWebhook()
    {
        Factory.Services.GetRequiredService<TestAuthState>().SetAuthenticatedUser("alice", ShortnrWebAppFactory.TestIssuer);
        await SeedUserAsync("alice", ShortnrWebAppFactory.TestIssuer);
        var webhookId = await SeedWebhookAsync("alice", ShortnrWebAppFactory.TestIssuer, "https://example.com/webhook", isActive: false);

        var client = Factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/settings/webhooks?handler=Activate", token,
            ("id", webhookId.ToString()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("re-enabled", body);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var webhook = await db.Webhooks.SingleAsync(w => w.Id == webhookId);
        Assert.True(webhook.IsActive);
        Assert.Equal(0, webhook.FailureCount);
    }

    [Fact]
    public async Task Webhooks_AreScopedToOwner()
    {
        var aliceFactory = new ShortnrWebAppFactory();
        aliceFactory.Services.GetRequiredService<TestAuthState>().SetAuthenticatedUser("alice", ShortnrWebAppFactory.TestIssuer);
        await SeedUserAsync("alice", ShortnrWebAppFactory.TestIssuer, aliceFactory);
        await SeedWebhookAsync("alice", ShortnrWebAppFactory.TestIssuer, "https://alice.example.com/webhook", factory: aliceFactory);

        var bobFactory = new ShortnrWebAppFactory();
        bobFactory.Services.GetRequiredService<TestAuthState>().SetAuthenticatedUser("bob", ShortnrWebAppFactory.TestIssuer);
        await SeedUserAsync("bob", ShortnrWebAppFactory.TestIssuer, bobFactory);
        await SeedWebhookAsync("bob", ShortnrWebAppFactory.TestIssuer, "https://bob.example.com/webhook", factory: bobFactory);

        var aliceClient = aliceFactory.CreateClient();
        var aliceResponse = await aliceClient.GetAsync("/settings/webhooks");
        var aliceBody = await aliceResponse.Content.ReadAsStringAsync();

        Assert.Contains("alice.example.com", aliceBody);
        Assert.DoesNotContain("bob.example.com", aliceBody);

        await aliceFactory.DisposeAsync();
        await bobFactory.DisposeAsync();
    }

    private async Task SeedUserAsync(string subject, string issuer, ShortnrWebAppFactory? factory = null)
    {
        factory ??= Factory;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (!await db.Users.AnyAsync(u => u.Issuer == issuer && u.Subject == subject))
        {
            db.Users.Add(new User
            {
                Issuer = issuer,
                Subject = subject,
                Email = $"{subject}@example.com",
                CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
    }

    private async Task<long> SeedWebhookAsync(string subject, string issuer, string url, bool isActive = true, ShortnrWebAppFactory? factory = null)
    {
        factory ??= Factory;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync(u => u.Issuer == issuer && u.Subject == subject);
        var webhook = new Webhook
        {
            OwnerUserId = user.Id,
            Url = url,
            Secret = WebhookSigningService.GenerateSecret(),
            EventTypes = WebhookEventTypes.LinkCreated,
            IsActive = isActive,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.Webhooks.Add(webhook);
        await db.SaveChangesAsync();
        return webhook.Id;
    }

    private static Task<string> GetAntiforgeryTokenAsync(HttpClient client) =>
        TestForms.GetAntiforgeryTokenAsync(client, "/settings/webhooks");

    private static Task<HttpResponseMessage> PostFormAsync(HttpClient client, string path, string token, params (string Name, string Value)[] fields) =>
        TestForms.PostFormAsync(client, path, token, fields);
}
