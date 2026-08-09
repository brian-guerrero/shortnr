using Shortnr.Data.Entities;

namespace Shortnr.Web.Features.Insights;

/// <summary>Minimal click projection the heuristics need; avoids pulling full rows.</summary>
public readonly record struct ClickDatum(string Referer, DateTime ClickedAtUtc);

/// <summary>A tag proposed by one of the local heuristics, pending owner review.</summary>
public sealed record TagSuggestionDraft(string Tag, TagSuggestionSource Source, long ClickCount, DateTime FirstObservedUtc);

/// <summary>
/// Local, deterministic heuristics (PRD-006) that propose tags from a short
/// link's click pattern. No external AI: referrer-domain clustering, UTM
/// parameter extraction from the flattened URL, and a high click-frequency
/// signal. Every heuristic is pure and unit-testable.
/// </summary>
public static class InsightHeuristics
{
    public const int MinReferrerClicksForSuggestion = 5;
    public const double ReferrerClusterRatio = 0.30;
    public const int HighFrequencyClickThreshold = 30;
    public const int MaxTagLength = 64;

    public static List<TagSuggestionDraft> Analyze(string longUrl, IReadOnlyCollection<ClickDatum> clicksInWindow)
    {
        var drafts = new List<TagSuggestionDraft>();
        if (clicksInWindow.Count == 0) return drafts;

        var firstObservedUtc = clicksInWindow.Min(c => c.ClickedAtUtc);

        AddReferrerClusters(clicksInWindow, drafts);
        AddUtmExtractions(longUrl, firstObservedUtc, drafts);
        AddHighFrequency(clicksInWindow.Count, firstObservedUtc, drafts);

        return drafts;
    }

    /// <summary>
    /// Suggests the dominant referrer hostname as a tag when a single domain
    /// accounts for at least <see cref="MinReferrerClicksForSuggestion"/> clicks
    /// and a <see cref="ReferrerClusterRatio"/> share of the window's traffic.
    /// </summary>
    private static void AddReferrerClusters(IReadOnlyCollection<ClickDatum> clicks, List<TagSuggestionDraft> drafts)
    {
        var total = clicks.Count;

        var clusters = clicks
            .Where(c => !string.IsNullOrWhiteSpace(c.Referer))
            .GroupBy(c => NormalizeReferrerHost(c.Referer), StringComparer.OrdinalIgnoreCase)
            .Select(g => new { Host = g.Key, Clicks = g.ToList() })
            .Where(g => g.Clicks.Count >= MinReferrerClicksForSuggestion);

        foreach (var cluster in clusters)
        {
            if ((double)cluster.Clicks.Count / total < ReferrerClusterRatio) continue;

            var tag = CleanTag(cluster.Host);
            if (tag.Length == 0) continue;

            drafts.Add(new TagSuggestionDraft(
                tag,
                TagSuggestionSource.ReferrerDomainCluster,
                cluster.Clicks.Count,
                cluster.Clicks.Min(c => c.ClickedAtUtc)));
        }
    }

    /// <summary>
    /// Suggests one tag per non-empty <c>utm_*</c> parameter found on the
    /// flattened URL, e.g. <c>?utm_source=facebook</c> → <c>facebook</c>.
    /// </summary>
    private static void AddUtmExtractions(string longUrl, DateTime firstObservedUtc, List<TagSuggestionDraft> drafts)
    {
        if (string.IsNullOrWhiteSpace(longUrl) || !Uri.TryCreate(longUrl, UriKind.Absolute, out var uri)) return;
        if (string.IsNullOrEmpty(uri.Query)) return;

        var seen = new HashSet<string>(drafts.Select(d => d.Tag), StringComparer.OrdinalIgnoreCase);

        foreach (var (_, value) in ParseUtmParameters(uri.Query))
        {
            var tag = CleanTag(value);
            if (tag.Length == 0 || seen.Contains(tag)) continue;

            seen.Add(tag);
            drafts.Add(new TagSuggestionDraft(tag, TagSuggestionSource.UtmExtraction, 0, firstObservedUtc));
        }
    }

    /// <summary>
    /// Suggests <c>trending</c> when a link's click count within the analysis
    /// window crosses <see cref="HighFrequencyClickThreshold"/>.
    /// </summary>
    private static void AddHighFrequency(int windowClicks, DateTime firstObservedUtc, List<TagSuggestionDraft> drafts)
    {
        const string trending = "trending";
        if (windowClicks < HighFrequencyClickThreshold) return;
        if (drafts.Any(d => d.Tag == trending)) return;

        drafts.Add(new TagSuggestionDraft(trending, TagSuggestionSource.HighFrequency, windowClicks, firstObservedUtc));
    }

    /// <summary>Lowercases and strips <c>www.</c> from a referer's hostname.</summary>
    public static string NormalizeReferrerHost(string referer)
    {
        if (string.IsNullOrWhiteSpace(referer) || !Uri.TryCreate(referer, UriKind.Absolute, out var uri))
            return string.Empty;

        var host = uri.Host;
        if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            host = host[4..];

        return host.ToLowerInvariant();
    }

    /// <summary>Yields non-empty <c>utm_*</c> query parameter values, percent-decoded.</summary>
    public static IEnumerable<(string Key, string Value)> ParseUtmParameters(string query)
    {
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var sep = pair.IndexOf('=');
            var key = sep < 0 ? pair : pair[..sep];
            var value = sep < 0 ? string.Empty : pair[(sep + 1)..];

            if (!key.StartsWith("utm_", StringComparison.OrdinalIgnoreCase)) continue;

            yield return (key.ToLowerInvariant(), Uri.UnescapeDataString(value));
        }
    }

    /// <summary>Normalizes a raw token into a displayable tag (lowercase, alphanumeric + dashes).</summary>
    public static string CleanTag(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        var cleaned = raw.ToLowerInvariant().Trim().Replace(' ', '-');
        cleaned = new string(cleaned.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_').ToArray());

        while (cleaned.Contains("--", StringComparison.Ordinal))
            cleaned = cleaned.Replace("--", "-", StringComparison.Ordinal);

        cleaned = cleaned.Trim('-', '_');
        return cleaned.Length > MaxTagLength ? cleaned[..MaxTagLength] : cleaned;
    }
}
