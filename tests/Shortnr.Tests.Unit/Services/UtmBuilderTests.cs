namespace Shortnr.Tests.Unit.Services;

public class UtmBuilderTests
{
    [Fact]
    public void AppendUtm_NullParameters_ReturnsUrlUnchanged()
    {
        var result = UtmBuilder.AppendUtm("https://example.com/page", null);

        Assert.Equal("https://example.com/page", result);
    }

    [Fact]
    public void AppendUtm_EmptyParameters_ReturnsUrlUnchanged()
    {
        var result = UtmBuilder.AppendUtm("https://example.com/page", new UtmParameters(null, null, null, null, null));

        Assert.Equal("https://example.com/page", result);
    }

    [Fact]
    public void AppendUtm_AppendsPopulatedComponentsAsQueryParameters()
    {
        var result = UtmBuilder.AppendUtm("https://example.com/page",
            new UtmParameters("newsletter", "email", "spring-sale-2026", "running-shoes", "hero-banner"));

        Assert.Equal(
            "https://example.com/page?utm_source=newsletter&utm_medium=email&utm_campaign=spring-sale-2026&utm_term=running-shoes&utm_content=hero-banner",
            result);
    }

    [Fact]
    public void AppendUtm_BlankComponent_IsSkipped()
    {
        var result = UtmBuilder.AppendUtm("https://example.com/page",
            new UtmParameters("newsletter", null, "sale", null, null));

        Assert.Equal("https://example.com/page?utm_source=newsletter&utm_campaign=sale", result);
    }

    [Fact]
    public void AppendUtm_MergesOverExistingQueryParameters()
    {
        var result = UtmBuilder.AppendUtm("https://example.com/page?utm_source=old&ref=xyz",
            new UtmParameters("new", "cpc", null, null, null));

        var query = ParseQuery(result);
        Assert.Equal("new", query["utm_source"]);
        Assert.Equal("cpc", query["utm_medium"]);
        Assert.Equal("xyz", query["ref"]);
        Assert.Equal(3, query.Count);
    }

    [Fact]
    public void AppendUtm_BlankUtmField_PreservesExistingParameter()
    {
        var result = UtmBuilder.AppendUtm("https://example.com/page?utm_source=existing&ref=xyz",
            new UtmParameters(null, "email", null, null, null));

        var query = ParseQuery(result);
        Assert.Equal("existing", query["utm_source"]);
        Assert.Equal("email", query["utm_medium"]);
        Assert.Equal("xyz", query["ref"]);
        Assert.Equal(3, query.Count);
    }

    [Fact]
    public void AppendUtm_UrlWithoutTrailingSlash_KeepsPath()
    {
        var result = UtmBuilder.AppendUtm("https://example.com/very/long/path?existing=1",
            new UtmParameters("campaign", null, null, null, null));

        Assert.Equal("https://example.com/very/long/path?existing=1&utm_source=campaign", result);
    }

    private static Dictionary<string, string> ParseQuery(string url)
    {
        var index = url.IndexOf('?');
        Assert.True(index >= 0, $"Expected a query string in '{url}'.");
        return url[(index + 1)..]
            .Split('&')
            .Select(pair =>
            {
                var kv = pair.Split('=', 2);
                return (kv[0], kv.Length == 2 ? kv[1] : "");
            })
            .ToDictionary(kv => kv.Item1, kv => kv.Item2);
    }
}
