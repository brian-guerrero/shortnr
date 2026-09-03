using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shortnr.Data;
using Shortnr.Data.Entities;

namespace Shortnr.Web.Features.Insights.Llm;

/// <summary>
/// Durable cost tracking and budget enforcement for the LLM layer. Every call is
/// written to <c>LlmUsageLog</c> (so budget checks survive restarts), and the
/// optional <c>AiInsights:Llm:MonthlyBudget</c> is enforced from that month-to-date
/// spend. Scoped because it writes through the request-scoped <see cref="AppDbContext"/>.
/// </summary>
public sealed class LlmUsageService(
    AppDbContext db,
    IOptions<LlmOptions> options,
    LlmPricing pricing,
    ILogger<LlmUsageService> logger)
{
    /// <summary>
    /// Returns false when the month-to-date spend plus the estimated prompt cost
    /// of this call would exceed the configured monthly budget. A budget of 0
    /// (the default) always returns true.
    /// </summary>
    public async Task<bool> IsWithinBudgetAsync(LlmRequest request, CancellationToken ct = default)
    {
        var cfg = options.Value;
        if (cfg.MonthlyBudget <= 0) return true;

        var used = await MonthToDateCostAsync(ct);
        var promptEstimate = LlmPricing.EstimatePromptTokens(request.SystemPrompt + request.UserPrompt);
        var expectedCost = pricing.EstimateCost(promptEstimate, 0, request.Model);
        return used + expectedCost <= cfg.MonthlyBudget;
    }

    /// <summary>Total estimated USD spend of LLM calls this calendar month.</summary>
    public async Task<decimal> MonthToDateCostAsync(CancellationToken ct = default)
    {
        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        return await db.LlmUsageLogs
            .Where(u => u.CreatedAtUtc >= monthStart)
            .SumAsync(u => (decimal?)u.EstimatedCostUsd, ct) ?? 0m;
    }

    /// <summary>Persists one usage row and writes a structured cost log line.</summary>
    public async Task RecordAsync(LlmUsageRecord record, CancellationToken ct = default)
    {
        db.LlmUsageLogs.Add(new LlmUsageLog
        {
            OwnerUserId = record.OwnerUserId,
            Provider = record.Provider,
            Model = record.Model,
            Operation = record.Operation,
            PromptTokens = record.PromptTokens,
            CompletionTokens = record.CompletionTokens,
            EstimatedCostUsd = record.EstimatedCostUsd,
            Success = record.Success,
            ErrorKind = record.ErrorKind,
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "LLM call {Operation} on {Provider}/{Model}: {Prompt} in / {Completion} out tokens, est ${Cost:F4}, success={Success}",
            record.Operation, record.Provider, record.Model,
            record.PromptTokens, record.CompletionTokens, record.EstimatedCostUsd, record.Success);
    }
}