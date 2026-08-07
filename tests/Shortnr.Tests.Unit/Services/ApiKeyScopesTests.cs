using System.Security.Claims;

namespace Shortnr.Tests.Unit.Services;

public class ApiKeyScopesTests
{
    [Fact]
    public void Resolve_WhenNull_ReturnsAllScopes()
    {
        Assert.Equal(ApiKeyScopes.All, ApiKeyScopes.Resolve(null));
    }

    [Fact]
    public void Resolve_WhenEmpty_ReturnsAllScopes()
    {
        Assert.Equal(ApiKeyScopes.All, ApiKeyScopes.Resolve(""));
    }

    [Fact]
    public void Resolve_WhenWhitespace_ReturnsAllScopes()
    {
        Assert.Equal(ApiKeyScopes.All, ApiKeyScopes.Resolve("   "));
    }

    [Fact]
    public void Resolve_WithKnownScopes_ReturnsOnlyThose()
    {
        var result = ApiKeyScopes.Resolve("links:read mcp:write");

        Assert.Equal(new[] { ApiKeyScopes.LinksRead, ApiKeyScopes.McpWrite }, result);
    }

    [Fact]
    public void Resolve_WithUnknownScope_ExcludesIt()
    {
        var result = ApiKeyScopes.Resolve("links:read bogus:scope mcp:read");

        Assert.Equal(new[] { ApiKeyScopes.LinksRead, ApiKeyScopes.McpRead }, result);
    }

    [Fact]
    public void Resolve_DeduplicatesRepeatedScopes()
    {
        var result = ApiKeyScopes.Resolve("links:read links:read");

        Assert.Single(result);
    }

    [Fact]
    public void IsValid_AcceptsKnownScope()
    {
        Assert.True(ApiKeyScopes.IsValid(ApiKeyScopes.McpRead));
    }

    [Fact]
    public void IsValid_RejectsUnknownScope()
    {
        Assert.False(ApiKeyScopes.IsValid("admin:*"));
    }

    [Fact]
    public void IsValidSelection_WhenEmpty_ReturnsFalse()
    {
        Assert.False(ApiKeyScopes.IsValidSelection(Array.Empty<string>()));
    }

    [Fact]
    public void IsValidSelection_WhenAnyUnknown_ReturnsFalse()
    {
        Assert.False(ApiKeyScopes.IsValidSelection([ApiKeyScopes.LinksRead, "nope"]));
    }

    [Fact]
    public void IsValidSelection_WhenAllKnown_ReturnsTrue()
    {
        Assert.True(ApiKeyScopes.IsValidSelection([ApiKeyScopes.McpRead, ApiKeyScopes.McpWrite]));
    }

    [Fact]
    public void Format_SortsAndDeduplicates()
    {
        Assert.Equal(
            "links:read links:write mcp:read",
            ApiKeyScopes.Format([ApiKeyScopes.McpRead, ApiKeyScopes.LinksWrite, ApiKeyScopes.LinksRead, ApiKeyScopes.McpRead]));
    }

    [Fact]
    public void Format_DropsUnknownScopes()
    {
        Assert.Equal(ApiKeyScopes.LinksRead, ApiKeyScopes.Format([ApiKeyScopes.LinksRead, "bogus"]));
    }

    [Fact]
    public void HasScope_ChecksTheScopeClaim()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ApiKeyScopes.ScopeClaim, ApiKeyScopes.McpRead)],
            authenticationType: "test"));

        Assert.True(ApiKeyScopes.HasScope(principal, ApiKeyScopes.McpRead));
        Assert.False(ApiKeyScopes.HasScope(principal, ApiKeyScopes.McpWrite));
    }
}
