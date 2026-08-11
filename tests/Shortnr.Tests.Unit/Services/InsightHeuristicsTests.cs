using Shortnr.Data.Entities;

namespace Shortnr.Tests.Unit.Services;

public class InsightHeuristicsTests
{
    private static ClickDatum Click(string referer, DateTime? at = null) =>
        new(referer, at ?? DateTime.UtcNow);

    // -------------------------------------------------------------------------
    // Empty input
    // -------------------------------------------------------------------------

    [Fact]
    public void Analyze_NoClicks_ReturnsEmpty()
    {
        var drafts = InsightHeuristics.Analyze("https://example.com", []);

        Assert.Empty(drafts);
    }

    // -------------------------------------------------------------------------
    // Referrer-domain clustering
    // -------------------------------------------------------------------------

    [Fact]
    public void Analyze_DominantReferrer_SuggestsHostname()
    {
        var clicks = Enumerable.Range(0, 10)
            .Select(i => Click(i < 7 ? "https://www.facebook.com/post/1" : "https://news.example.com/x"))
            .ToList();

        var drafts = InsightHeuristics.Analyze("https://example.com", clicks);

        var draft = Assert.Single(drafts, d => d.Source == TagSuggestionSource.ReferrerDomainCluster);
        Assert.Equal("facebook.com", draft.Tag);
        Assert.Equal(7, draft.ClickCount);
    }

    [Fact]
    public void Analyze_ReferrerBelowMinClicks_NotSuggested()
    {
        var clicks = Enumerable.Range(0, 10)
            .Select(i => Click(i < 4 ? "https://facebook.com/x" : $"https://news{i % 5}.example.com/y"))
            .ToList();

        var drafts = InsightHeuristics.Analyze("https://example.com", clicks);

        Assert.DoesNotContain(drafts, d => d.Source == TagSuggestionSource.ReferrerDomainCluster);
    }

    [Fact]
    public void Analyze_ReferrerBelowRatio_NotSuggested()
    {
        // 5 clicks from one host out of 20 = 25%, under the 30% cluster ratio.
        var clicks = Enumerable.Range(0, 20)
            .Select(i => Click(i < 5 ? "https://facebook.com/x" : $"https://news{i % 5}.example.com/y"))
            .ToList();

        var drafts = InsightHeuristics.Analyze("https://example.com", clicks);

        Assert.DoesNotContain(drafts, d => d.Source == TagSuggestionSource.ReferrerDomainCluster);
    }

    [Fact]
    public void Analyze_EmptyAndBlankReferers_AreIgnored()
    {
        var clicks = Enumerable.Range(0, 10)
            .Select(i => Click(i < 7 ? "" : "https://facebook.com/x"))
            .ToList();

        var drafts = InsightHeuristics.Analyze("https://example.com", clicks);

        Assert.DoesNotContain(drafts, d => d.Source == TagSuggestionSource.ReferrerDomainCluster);
    }

    // -------------------------------------------------------------------------
    // UTM extraction
    // -------------------------------------------------------------------------

    [Fact]
    public void Analyze_UtmParameters_AreExtractedAsTags()
    {
        var clicks = Enumerable.Range(0, 10).Select(_ => Click("https://news.example.com")).ToList();
        var drafts = InsightHeuristics.Analyze(
            "https://example.com/landing?utm_source=facebook&utm_medium=cpc&ref=x", clicks);

        Assert.Contains(drafts, d => d.Tag == "facebook" && d.Source == TagSuggestionSource.UtmExtraction);
        Assert.Contains(drafts, d => d.Tag == "cpc" && d.Source == TagSuggestionSource.UtmExtraction);
    }

    [Fact]
    public void Analyze_EncodedUtmValue_IsDecodedAndCleaned()
    {
        var clicks = Enumerable.Range(0, 10).Select(_ => Click("https://news.example.com")).ToList();
        var drafts = InsightHeuristics.Analyze(
            "https://example.com/landing?utm_campaign=spring%20sale%202026", clicks);

        Assert.Contains(drafts, d => d.Tag == "spring-sale-2026" && d.Source == TagSuggestionSource.UtmExtraction);
    }

    [Fact]
    public void Analyze_DuplicateUtmValues_AreSuggestedOnce()
    {
        var clicks = Enumerable.Range(0, 10).Select(_ => Click("https://news.example.com")).ToList();
        var drafts = InsightHeuristics.Analyze(
            "https://example.com/landing?utm_source=facebook&utm_medium=facebook", clicks);

        Assert.Single(drafts, d => d.Tag == "facebook");
    }

    // -------------------------------------------------------------------------
    // High-frequency signal
    // -------------------------------------------------------------------------

    [Fact]
    public void Analyze_AtHighFrequencyThreshold_SuggestsTrending()
    {
        var clicks = Enumerable.Range(0, 30).Select(_ => Click("https://news.example.com")).ToList();
        var drafts = InsightHeuristics.Analyze("https://example.com", clicks);

        Assert.Contains(drafts, d => d.Tag == "trending" && d.Source == TagSuggestionSource.HighFrequency);
    }

    [Fact]
    public void Analyze_BelowHighFrequencyThreshold_NoTrending()
    {
        var clicks = Enumerable.Range(0, 29).Select(_ => Click("https://news.example.com")).ToList();
        var drafts = InsightHeuristics.Analyze("https://example.com", clicks);

        Assert.DoesNotContain(drafts, d => d.Tag == "trending");
    }

    // -------------------------------------------------------------------------
    // Normalization helpers
    // -------------------------------------------------------------------------

    [Fact]
    public void NormalizeReferrerHost_StripsSchemePathWwwAndLowercases()
    {
        Assert.Equal("facebook.com", InsightHeuristics.NormalizeReferrerHost("https://www.Facebook.com/post/1"));
        Assert.Equal("example.com", InsightHeuristics.NormalizeReferrerHost("https://EXAMPLE.com"));
    }

    [Fact]
    public void NormalizeReferrerHost_InvalidInput_ReturnsEmpty()
    {
        Assert.Equal("", InsightHeuristics.NormalizeReferrerHost(""));
        Assert.Equal("", InsightHeuristics.NormalizeReferrerHost("not a url"));
    }

    [Fact]
    public void CleanTag_NormalizesCaseSpacesAndPunctuation()
    {
        Assert.Equal("facebook.com", InsightHeuristics.CleanTag("Facebook.com"));
        Assert.Equal("spring-sale", InsightHeuristics.CleanTag("  Spring Sale  "));
        Assert.Equal("c.p.c", InsightHeuristics.CleanTag("c.p.c!"));
    }
}
