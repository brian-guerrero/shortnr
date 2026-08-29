using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;

namespace Shortnr.Web.Features.Insights.Llm;

/// <summary>
/// Persists and loads the "Ask AI" history shown on /insights, so a page refresh (or later
/// visit) still shows past runs instead of just the most recent htmx swap. Distinct from
/// <see cref="LlmUsageService"/>: that one is the cost/budget audit trail (tokens, estimated
/// cost), written regardless of what the UI does with the result; this one is the user-facing
/// content history, written by the page model after it already has the result and the raw
/// input the user typed. Scoped like <see cref="LlmUsageService"/> since it writes through the
/// request-scoped <see cref="AppDbContext"/>.
/// </summary>
public sealed class LlmInsightHistoryService(AppDbContext db)
{
    public async Task RecordAsync(long? ownerUserId, LlmOperation operation, string inputSummary, LlmInsightResult result, CancellationToken ct = default)
    {
        db.LlmInsightRuns.Add(new LlmInsightRun
        {
            OwnerUserId = ownerUserId,
            Operation = operation.ToString(),
            InputSummary = inputSummary,
            Success = result.IsSuccess,
            Content = result.IsSuccess ? result.Content : null,
            FriendlyMessage = result.IsSuccess ? null : result.FriendlyMessage,
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Most recent runs for the owner, newest first. <paramref name="ownerUserId"/> may
    /// be null when auth is disabled -- every row is then also written with a null owner, so a
    /// plain equality filter still scopes correctly to "everything" in that single-tenant case.</summary>
    public async Task<List<LlmInsightRunRow>> RecentAsync(long? ownerUserId, int take = 20, CancellationToken ct = default)
    {
        // Materialize first, then map -- Enum.Parse can't be translated into SQL, so the
        // LlmOperation conversion has to happen client-side over the (small, Take-limited) result.
        var rows = await db.LlmInsightRuns.AsNoTracking()
            .Where(r => r.OwnerUserId == ownerUserId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ThenByDescending(r => r.Id)
            .Take(take)
            .ToListAsync(ct);

        return rows.Select(r => new LlmInsightRunRow
        {
            Id = r.Id,
            Operation = Enum.Parse<LlmOperation>(r.Operation),
            InputSummary = r.InputSummary,
            Success = r.Success,
            Content = r.Content,
            FriendlyMessage = r.FriendlyMessage,
            CreatedAtUtc = r.CreatedAtUtc
        }).ToList();
    }
}
