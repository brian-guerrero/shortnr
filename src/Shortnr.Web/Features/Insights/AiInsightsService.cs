using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shortnr.Data;
using Shortnr.Data.Entities;

namespace Shortnr.Web.Features.Insights;

/// <summary>
/// Runs the PRD-006 analysis pass: finds links that have accumulated enough
/// clicks, applies <see cref="InsightHeuristics"/> to their recent click events,
/// and persists new pending <see cref="TagSuggestion"/> rows. Idempotent — a tag
/// already suggested for a link (pending, accepted or dismissed) is never
/// re-proposed. Called by <see cref="AiInsightsHostedService"/> on a timer.
/// </summary>
public sealed class AiInsightsService(AppDbContext db, IOptions<AiInsightsOptions> options)
{
    /// <summary>Returns the number of new suggestion rows created.</summary>
    public async Task<int> RunAnalysisAsync(CancellationToken ct = default)
    {
        var cfg = options.Value;
        var nowUtc = DateTime.UtcNow;
        var windowStart = nowUtc.AddHours(-Math.Max(1, cfg.AnalysisIntervalHours));

        var candidates = await db.ShortenedUrls
            .AsNoTracking()
            .Where(l => l.ClickCount >= cfg.MinClicksForAnalysis)
            .Select(l => new { l.Id, l.LongUrl })
            .ToListAsync(ct);

        if (candidates.Count == 0)
            return 0;

        var candidateIds = candidates.Select(c => c.Id).ToList();

        var clicksById = (await db.ClickEvents
                .AsNoTracking()
                .Where(c => candidateIds.Contains(c.ShortenedUrlId) && c.ClickedAtUtc >= windowStart)
                .Select(c => new { c.ShortenedUrlId, c.Referer, c.ClickedAtUtc })
                .ToListAsync(ct))
            .ToLookup(c => c.ShortenedUrlId, c => new ClickDatum(c.Referer, c.ClickedAtUtc));

        var existingTagsById = (await db.TagSuggestions
                .AsNoTracking()
                .Where(s => candidateIds.Contains(s.ShortenedUrlId))
                .Select(s => new { s.ShortenedUrlId, s.SuggestedTag })
                .ToListAsync(ct))
            .ToLookup(s => s.ShortenedUrlId, s => s.SuggestedTag);

        var created = 0;
        foreach (var candidate in candidates)
        {
            var clicks = clicksById[candidate.Id].ToList();
            var drafts = InsightHeuristics.Analyze(candidate.LongUrl, clicks);
            if (drafts.Count == 0) continue;

            var existingTags = new HashSet<string>(existingTagsById[candidate.Id]);
            var rows = drafts
                .Where(d => !existingTags.Contains(d.Tag))
                .Select(d => new TagSuggestion
                {
                    ShortenedUrlId = candidate.Id,
                    SuggestedTag = d.Tag,
                    Source = d.Source,
                    ClickCount = d.ClickCount,
                    FirstObservedUtc = d.FirstObservedUtc,
                    Status = TagSuggestionStatus.Pending,
                    CreatedAtUtc = nowUtc
                })
                .ToList();

            if (rows.Count == 0) continue;

            db.TagSuggestions.AddRange(rows);
            created += rows.Count;
        }

        if (created > 0)
            await db.SaveChangesAsync(ct);

        return created;
    }
}
