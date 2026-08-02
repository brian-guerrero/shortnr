using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Tests.Integration.Infrastructure;
using Shortnr.Web.Services;

namespace Shortnr.Tests.Integration.Mcp;

/// <summary>
/// Shared scaffolding for MCP integration tests: JSON-RPC posting over the stateless
/// HTTP transport, SSE data extraction, and DB seeding of users, API keys, links,
/// click events and bio pages.
/// </summary>
public abstract class McpTestBase : IAsyncLifetime
{
    protected const string ProtocolVersion = "2025-06-18";

    protected ShortnrWebAppFactory Factory { get; } = new(authEnabled: true);

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => Factory.DisposeAsync().AsTask();

    protected async Task<long> SeedUserAsync(string subject)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new User
        {
            Issuer = ShortnrWebAppFactory.TestIssuer,
            Subject = subject,
            CreatedAtUtc = DateTime.UtcNow,
            LastLoginAtUtc = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    protected async Task<long> SeedUserAndKeyAsync(string subject, string key, string scopes)
    {
        var userId = await SeedUserAsync(subject);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.ApiKeys.Add(new ApiKey
        {
            OwnerUserId = userId,
            KeyHash = ApiKeyService.HashKey(key),
            KeyPrefix = "snr_",
            Label = "mcp test key",
            Scopes = scopes,
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return userId;
    }

    protected HttpClient CreateAuthorizedClient(string key)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return client;
    }

    protected async Task<long> SeedLinkAsync(long ownerUserId, string shortCode, string longUrl, long clicks = 0)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var link = new ShortenedUrl
        {
            OwnerUserId = ownerUserId,
            ShortCode = shortCode,
            LongUrl = longUrl,
            DomainId = null,
            ClickCount = clicks,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.ShortenedUrls.Add(link);
        await db.SaveChangesAsync();
        return link.Id;
    }

    protected async Task SeedClickAsync(long linkId, string? referer = null, string? country = null,
        string? device = null, string? browser = null, DateTime? at = null)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.ClickEvents.Add(new ClickEvent
        {
            ShortenedUrlId = linkId,
            IpAddress = "1.2.3.4",
            UserAgent = "test",
            Referer = referer ?? "",
            ClickedAtUtc = at ?? DateTime.UtcNow,
            CountryName = country,
            DeviceFamily = device,
            Browser = browser
        });
        await db.SaveChangesAsync();
    }

    protected async Task<long> SeedBioPageAsync(long ownerUserId, string slug, string displayName, string theme = "default")
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var page = new BioPage
        {
            OwnerUserId = ownerUserId,
            Slug = slug,
            DisplayName = displayName,
            Theme = theme,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.BioPages.Add(page);
        await db.SaveChangesAsync();
        return page.Id;
    }

    /// <summary>Polls until an AiActivityLog row matching the owner+action appears (the
    /// processor drains its channel asynchronously) or fails after the timeout.</summary>
    protected async Task WaitForActivityAsync(long ownerUserId, string action, int timeoutMs = 5000)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (await db.AiActivityLogs.AnyAsync(a => a.OwnerUserId == ownerUserId && a.Action == action))
                return;
            await Task.Delay(100);
        }
        Assert.Fail($"No AiActivityLog entry for owner {ownerUserId} action '{action}' within {timeoutMs}ms.");
    }

    protected async Task AddBioPageLinkAsync(long bioPageId, long linkId, string title, int sortOrder, bool isVisible = true)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.BioPageLinks.Add(new BioPageLink
        {
            BioPageId = bioPageId,
            ShortenedUrlId = linkId,
            Title = title,
            SortOrder = sortOrder,
            IsVisible = isVisible
        });
        await db.SaveChangesAsync();
    }

    protected static async Task<HttpResponseMessage> PostJsonRpcAsync(HttpClient client, string method, string @params)
    {
        var body = $$"""{"jsonrpc":"2.0","id":1,"method":"{{method}}","params":{{@params}}}""";
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", ProtocolVersion);
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
        return await client.SendAsync(request);
    }

    protected static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        // Stateless streamable HTTP wraps single responses in an SSE frame even
        // when only one message is emitted; lift the JSON out of the data: lines.
        var payload = string.Join("", text.Split('\n')
            .Where(line => line.StartsWith("data:", StringComparison.Ordinal))
            .Select(line => line["data:".Length..].Trim()));
        return JsonDocument.Parse(payload.Length > 0 ? payload : text);
    }

    /// <summary>Returns the text content of a successful tool result.</summary>
    protected static string ToolText(JsonElement result)
    {
        var text = result.GetProperty("content")[0].GetProperty("text").GetString();
        Assert.False(string.IsNullOrEmpty(text), "Tool result text was empty.");
        return text!;
    }
}
