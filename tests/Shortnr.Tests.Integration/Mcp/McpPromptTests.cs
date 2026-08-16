using System.Net;
using System.Text.Json;

namespace Shortnr.Tests.Integration.Mcp;

/// <summary>
/// Exercises the PRD-020 prompt templates (<c>getting_started</c>,
/// <c>create_bio_page</c>): discovery via <c>prompts/list</c>, retrieval via
/// <c>prompts/get</c>, and the read-scope requirement.
/// </summary>
public class McpPromptTests : McpTestBase
{
    private const string TestKey = "snr_mcptest1234567890abcdef1234567890";

    [Fact]
    public async Task PromptsList_ExposesGettingStartedAndCreateBioPage()
    {
        await SeedUserAndKeyAsync("prompt-discover", TestKey, ApiKeyScopes.McpRead);
        var client = CreateAuthorizedClient(TestKey);

        var response = await PostJsonRpcAsync(client, "prompts/list", "{}");

        using var json = await ReadJsonAsync(response);
        var prompts = json.RootElement.GetProperty("result").GetProperty("prompts");
        var names = prompts.EnumerateArray().Select(p => p.GetProperty("name").GetString()).ToArray();
        Assert.Contains("getting_started", names);
        Assert.Contains("create_bio_page", names);
    }

    [Fact]
    public async Task PromptsGet_GettingStarted_ReturnsGuidedMarkdown()
    {
        await SeedUserAndKeyAsync("prompt-guide", TestKey, ApiKeyScopes.McpRead);
        var client = CreateAuthorizedClient(TestKey);

        var response = await PostJsonRpcAsync(client, "prompts/get", """{"name":"getting_started"}""");

        using var json = await ReadJsonAsync(response);
        var result = json.RootElement.GetProperty("result");
        Assert.Equal("Guided onboarding prompt: what shortnr does, how to shorten a link, and how to inspect links, clicks and bio pages.", result.GetProperty("description").GetString());
        var text = result.GetProperty("messages")[0].GetProperty("content").GetProperty("text").GetString();
        Assert.Contains("# Getting started with shortnr", text);
        Assert.Contains("create_short_link", text);
        Assert.Contains("shortnr://links", text);
    }

    [Fact]
    public async Task PromptsGet_CreateBioPage_ReturnsSteps()
    {
        await SeedUserAndKeyAsync("prompt-bio", TestKey, ApiKeyScopes.McpRead);
        var client = CreateAuthorizedClient(TestKey);

        var response = await PostJsonRpcAsync(client, "prompts/get", """{"name":"create_bio_page"}""");

        using var json = await ReadJsonAsync(response);
        var text = json.RootElement.GetProperty("result").GetProperty("messages")[0]
            .GetProperty("content").GetProperty("text").GetString();
        Assert.Contains("# Create a link-in-bio page", text);
        Assert.Contains("set_bio_page_text", text);
        Assert.Contains("reorder_bio_page", text);
    }

    [Fact]
    public async Task Prompts_RequireMcpReadScope()
    {
        await SeedUserAndKeyAsync("prompt-writeonly", TestKey, ApiKeyScopes.McpWrite);
        var client = CreateAuthorizedClient(TestKey);

        var response = await PostJsonRpcAsync(client, "prompts/get", """{"name":"getting_started"}""");

        using var json = await ReadJsonAsync(response);
        var text = json.RootElement.GetProperty("result").GetProperty("messages")[0]
            .GetProperty("content").GetProperty("text").GetString();
        Assert.Contains(ApiKeyScopes.McpRead, text);
    }

    [Fact]
    public async Task PromptsGet_UnknownName_ReturnsJsonRpcError()
    {
        await SeedUserAndKeyAsync("prompt-unknown", TestKey, ApiKeyScopes.McpRead);
        var client = CreateAuthorizedClient(TestKey);

        var response = await PostJsonRpcAsync(client, "prompts/get", """{"name":"no_such_prompt"}""");

        using var json = await ReadJsonAsync(response);
        Assert.True(json.RootElement.TryGetProperty("error", out _), "Expected a JSON-RPC error for an unknown prompt.");
    }
}