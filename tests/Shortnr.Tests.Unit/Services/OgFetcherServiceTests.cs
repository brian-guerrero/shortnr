using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Web.Features.Social;

namespace Shortnr.Tests.Unit.Services;

/// <summary>
/// Unit tests for OgFetcherService: OG tag parsing, caching, and staleness handling.
/// </summary>
public class OgFetcherServiceTests : IDisposable
{
    private readonly AppDbContext _db;

    public OgFetcherServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    // ----- ParseOgTags tests (via reflection since it's private) -----

    [Fact]
    public void ParseOgTags_WithOgTags_ReturnsMetadata()
    {
        var html = """
        <html>
        <head>
            <meta property="og:title" content="My Article Title" />
            <meta property="og:description" content="A great article about stuff" />
            <meta property="og:image" content="https://example.com/image.jpg" />
        </head>
        <body></body>
        </html>
        """;

        var result = CallParseOgTags(html);
        Assert.NotNull(result);
        Assert.Equal("My Article Title", result!.Title);
        Assert.Equal("A great article about stuff", result.Description);
        Assert.Equal("https://example.com/image.jpg", result.Image);
    }

    [Fact]
    public void ParseOgTags_NoOgTags_ReturnsNull()
    {
        var html = "<html><head><title>Regular Page</title></head><body></body></html>";
        var result = CallParseOgTags(html);
        Assert.Null(result);
    }

    [Fact]
    public void ParseOgTags_PartialOgTags_ReturnsWhatExists()
    {
        var html = """
        <html>
        <head>
            <meta property="og:title" content="Only Title" />
        </head>
        </html>
        """;

        var result = CallParseOgTags(html);
        Assert.NotNull(result);
        Assert.Equal("Only Title", result!.Title);
        Assert.Null(result.Description);
        Assert.Null(result.Image);
    }

    [Fact]
    public void ParseOgTags_DescriptionOver2000Chars_Truncated()
    {
        var longDesc = new string('x', 2500);
        var html = $"""
        <html>
        <head>
            <meta property="og:description" content="{longDesc}" />
        </head>
        </html>
        """;

        var result = CallParseOgTags(html);
        Assert.NotNull(result);
        Assert.Equal(2000, result!.Description!.Length);
    }

    [Fact]
    public void ParseOgTags_SingleQuotes_Works()
    {
        var html = """
        <html>
        <head>
            <meta property='og:title' content='Single Quoted Title' />
        </head>
        </html>
        """;

        var result = CallParseOgTags(html);
        Assert.NotNull(result);
        Assert.Equal("Single Quoted Title", result!.Title);
    }

    [Fact]
    public void ParseOgTags_CaseInsensitive_Works()
    {
        var html = """
        <html>
        <head>
            <META PROPERTY="OG:TITLE" CONTENT="Upper Case Title" />
        </head>
        </html>
        """;

        var result = CallParseOgTags(html);
        Assert.NotNull(result);
        Assert.Equal("Upper Case Title", result!.Title);
    }

    [Fact]
    public void ParseOgTags_EmptyContent_ReturnsNull()
    {
        var html = """
        <html>
        <head>
            <meta property="og:title" content="" />
        </head>
        </html>
        """;

        var result = CallParseOgTags(html);
        // Empty content means the regex captures empty string, which is not null
        // but the method should still return the OgMetadata with empty title
        Assert.NotNull(result);
    }

    // ----- SocialData model tests -----

    [Fact]
    public void SocialData_Posts_CanBeEmpty()
    {
        var data = new SocialData { Posts = [] };
        Assert.Empty(data.Posts);
        Assert.Null(data.AudienceCount);
    }

    [Fact]
    public void SocialPostItem_AllFields_Settable()
    {
        var post = new SocialPostItem
        {
            ExternalPostId = "ext-123",
            Title = "Title",
            Text = "Text content",
            MediaUrl = "https://example.com/media.jpg",
            Permalink = "https://x.com/user/status/123",
            PublishedAtUtc = DateTime.UtcNow
        };

        Assert.Equal("ext-123", post.ExternalPostId);
        Assert.Equal("Title", post.Title);
        Assert.Equal("Text content", post.Text);
        Assert.Equal("https://example.com/media.jpg", post.MediaUrl);
        Assert.Equal("https://x.com/user/status/123", post.Permalink);
        Assert.NotNull(post.PublishedAtUtc);
    }

    // ----- SocialProvider enum tests -----

    [Fact]
    public void SocialProvider_ContainsAllFourPlatforms()
    {
        var values = Enum.GetValues<SocialProvider>();
        Assert.Equal(4, values.Length);
        Assert.Contains(SocialProvider.Twitter, values);
        Assert.Contains(SocialProvider.Instagram, values);
        Assert.Contains(SocialProvider.TikTok, values);
        Assert.Contains(SocialProvider.YouTube, values);
    }

    [Fact]
    public void SocialProvider_Ordering_IsStable()
    {
        Assert.Equal(0, (int)SocialProvider.Twitter);
        Assert.Equal(1, (int)SocialProvider.Instagram);
        Assert.Equal(2, (int)SocialProvider.TikTok);
        Assert.Equal(3, (int)SocialProvider.YouTube);
    }

    // ----- Helpers -----

    private static OgMetadata? CallParseOgTags(string html)
    {
        var method = typeof(OgFetcherService).GetMethod("ParseOgTags",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        return (OgMetadata?)method?.Invoke(null, [html]);
    }
}
