using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shortnr.Data;
using Shortnr.Data.Entities;

namespace Shortnr.Tests.Unit.Services;

public class LlmUsageServiceTests : IDisposable
{
    private readonly AppDbContext _db;

    public LlmUsageServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    private LlmUsageService Build(decimal monthlyBudget = 0) =>
        new(_db, Options.Create(new LlmOptions
        {
            Enabled = true,
            Provider = LlmProvider.OpenAi,
            Model = "gpt-4o-mini",
            MonthlyBudget = monthlyBudget
        }), new LlmPricing(Options.Create(new LlmOptions { Enabled = true })), NullLogger<LlmUsageService>.Instance);

    private static LlmRequest Req() =>
        new(LlmOperation.AnalyzeTraffic, "system prompt", "user prompt", "gpt-4o-mini", 0.2, 512);

    [Fact]
    public async Task RecordAsync_PersistsTokensAndCost()
    {
        var usage = Build();

        await usage.RecordAsync(new LlmUsageRecord("OpenAI", "gpt-4o-mini", "analyze", 100, 20, 0.000123m, true, null, 7));

        var row = Assert.Single(_db.LlmUsageLogs);
        Assert.Equal("gpt-4o-mini", row.Model);
        Assert.Equal("analyze", row.Operation);
        Assert.Equal(100, row.PromptTokens);
        Assert.Equal(20, row.CompletionTokens);
        Assert.Equal(0.000123m, row.EstimatedCostUsd);
        Assert.True(row.Success);
        Assert.Equal(7, row.OwnerUserId);
    }

    [Fact]
    public async Task MonthToDateCostAsync_CountsOnlyCurrentMonth()
    {
        var usage = Build();
        var past = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-1);
        _db.LlmUsageLogs.AddRange(
            new LlmUsageLog { Provider = "OpenAI", Model = "m", Operation = "x", EstimatedCostUsd = 0.50m, CreatedAtUtc = DateTime.UtcNow },
            new LlmUsageLog { Provider = "OpenAI", Model = "m", Operation = "x", EstimatedCostUsd = 0.30m, CreatedAtUtc = DateTime.UtcNow },
            new LlmUsageLog { Provider = "OpenAI", Model = "m", Operation = "x", EstimatedCostUsd = 9.00m, CreatedAtUtc = past });
        await _db.SaveChangesAsync();

        var total = await usage.MonthToDateCostAsync();

        Assert.Equal(0.80m, total);
    }

    [Fact]
    public async Task IsWithinBudget_WhenBudgetZero_AlwaysAllows()
    {
        var usage = Build(monthlyBudget: 0);
        _db.LlmUsageLogs.Add(new LlmUsageLog { Provider = "OpenAI", Model = "m", Operation = "x", EstimatedCostUsd = 999m, CreatedAtUtc = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        Assert.True(await usage.IsWithinBudgetAsync(Req()));
    }

    [Fact]
    public async Task IsWithinBudget_AtBudgetWithAnyExpectedCost_Blocks()
    {
        var usage = Build(monthlyBudget: 1.00m);
        _db.LlmUsageLogs.Add(new LlmUsageLog { Provider = "OpenAI", Model = "m", Operation = "x", EstimatedCostUsd = 1.00m, CreatedAtUtc = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        Assert.False(await usage.IsWithinBudgetAsync(Req()));
    }

    [Fact]
    public async Task IsWithinBudget_BelowBudget_Allows()
    {
        var usage = Build(monthlyBudget: 100m);
        _db.LlmUsageLogs.Add(new LlmUsageLog { Provider = "OpenAI", Model = "m", Operation = "x", EstimatedCostUsd = 1.00m, CreatedAtUtc = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        Assert.True(await usage.IsWithinBudgetAsync(Req()));
    }

    [Fact]
    public async Task RecordAsync_ThenBudgetCheck_ReflectsNewSpend()
    {
        var usage = Build(monthlyBudget: 1.00m);
        await usage.RecordAsync(new LlmUsageRecord("OpenAI", "gpt-4o-mini", "analyze", 100, 20, 1.00m, true, null, null));

        Assert.False(await usage.IsWithinBudgetAsync(Req()));
    }
}