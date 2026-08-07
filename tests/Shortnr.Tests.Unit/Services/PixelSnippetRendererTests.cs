using Shortnr.Data;
using Shortnr.Data.Entities;

namespace Shortnr.Tests.Unit.Services;

public class PixelSnippetRendererTests
{
    [Fact]
    public void Render_TemplateSnippet_SubstitutesPixelId()
    {
        var snippet = new PixelSnippet
        {
            Id = 1,
            Name = "Meta Pixel",
            IsCustom = false,
            SnippetTemplate = "fbq('init', '{{PIXEL_ID}}');"
        };

        var html = PixelSnippetRenderer.Render(snippet, "1234567890");

        Assert.Equal("fbq('init', '1234567890');", html);
    }

    [Fact]
    public void Render_CustomSnippet_EmitsRawHtmlVerbatim()
    {
        var snippet = new PixelSnippet { Id = 3, Name = "Custom snippet", IsCustom = true, SnippetTemplate = "" };
        const string raw = "<script>console.log('pixel fired');</script>";

        var html = PixelSnippetRenderer.Render(snippet, raw);

        Assert.Equal(raw, html);
    }

    [Fact]
    public void Render_TemplateWithoutPixelId_ReturnsEmpty()
    {
        var snippet = new PixelSnippet
        {
            Id = 1,
            Name = "Meta Pixel",
            IsCustom = false,
            SnippetTemplate = "fbq('init', '{{PIXEL_ID}}');"
        };

        var html = PixelSnippetRenderer.Render(snippet, null);

        Assert.Equal("", html);
    }

    [Fact]
    public void Render_CustomWithoutHtml_ReturnsEmpty()
    {
        var snippet = new PixelSnippet { Id = 3, Name = "Custom snippet", IsCustom = true, SnippetTemplate = "" };

        var html = PixelSnippetRenderer.Render(snippet, "");

        Assert.Equal("", html);
    }

    [Fact]
    public void PlaceholderConstant_MatchesTemplateToken()
    {
        Assert.Equal("{{PIXEL_ID}}", PixelSnippetTemplates.Placeholder);
    }
}
