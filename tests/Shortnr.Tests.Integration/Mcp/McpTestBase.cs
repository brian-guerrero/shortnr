using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Tests.Integration.Infrastructure;

namespace Shortnr.Tests.Integration.Mcp;

/// <summary>
/// MCP-specific scaffolding on top of <see cref="IntegrationTestBase"/>: JSON-RPC
/// posting over the stateless HTTP transport, SSE data extraction, and seeding of
/// API keys and bio pages.
/// </summary>
public abstract class McpTestBase : IntegrationTestBase
{
    protected const string ProtocolVersion = "2025-06-18";

    protected async Task<long> SeedUserAndKeyAsync(string subject, string key, string scopes)
    {
        var userId = await SeedUserAsync(subject);
        await WithDbAsync(async db =>
        {
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
        });
        return userId;
    }

    protected HttpClient CreateAuthorizedClient(string key)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return client;
    }

    protected Task<long> SeedBioPageAsync(long ownerUserId, string slug, string displayName, string theme = "default") =>
        WithDbAsync(async db =>
        {
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
        });

    /// <summary>Polls until an AiActivityLog row matching the owner+action appears (the
    /// processor drains its channel asynchronously) or fails after the timeout.</summary>
    protected async Task WaitForActivityAsync(long ownerUserId, string action, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var seen = await WithDbAsync(db =>
                db.AiActivityLogs.AnyAsync(a => a.OwnerUserId == ownerUserId && a.Action == action));
            if (seen) return;
            await Task.Delay(100);
        }
        Assert.Fail($"No AiActivityLog entry for owner {ownerUserId} action '{action}' within {timeoutMs}ms.");
    }

    protected Task AddBioPageLinkAsync(long bioPageId, long linkId, string title, int sortOrder, bool isVisible = true) =>
        WithDbAsync(async db =>
        {
            db.BioPageLinks.Add(new BioPageLink
            {
                BioPageId = bioPageId,
                ShortenedUrlId = linkId,
                Title = title,
                SortOrder = sortOrder,
                IsVisible = isVisible
            });
            await db.SaveChangesAsync();
        });

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
