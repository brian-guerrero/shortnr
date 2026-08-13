using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shortnr.Data;
using Shortnr.Data.Entities;

namespace Shortnr.Tests.Unit.Services;

public class LlmInsightServiceTests : IDisposable
{
    private readonly AppDbContext _db;

    public LlmInsightServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    private LlmInsightService Build(LlmOptions options, FakeLlmClient client) =>
        new(client,
            new LlmUsageService(_db, Options.Create(options), new LlmPricing(Options.Create(options)),
                NullLogger<LlmUsageService>.Instance),
            new LlmPricing(Options.Create(options)),
            Options.Create(options),
            NullLogger<LlmInsightService>.Instance);

    private static LlmOptions EnabledOptions(decimal monthlyBudget = 0) => new()
    {
        Enabled = true,
        Provider = LlmProvider.OpenAi,
        ApiKey = "key",
        Model = "gpt-4o-mini",
        MonthlyBudget = monthlyBudget
    };

    private static LlmRequest Req() =>
        new(LlmOperation.AnalyzeTraffic, "system prompt", "user prompt", "gpt-4o-mini", 0.2, 512);

    [Fact]
    public async Task CompleteAsync_Success_ReturnsContentAndRecordsUsage()
    {
        var client = new FakeLlmClient { Result = new LlmCompletion("Traffic spiked from Reddit.", 120, 30, "gpt-4o-mini") };
        var service = Build(EnabledOptions(), client);

        var result = await service.CompleteAsync(Req(), ownerUserId: 3);

        Assert.Equal(LlmInsightStatus.Success, result.Status);
        Assert.Equal("Traffic spiked from Reddit.", result.Content);
        Assert.Equal(1, client.Calls);

        var row = Assert.Single(_db.LlmUsageLogs);
        Assert.True(row.Success);
        Assert.Equal(120, row.PromptTokens);
        Assert.Equal(30, row.CompletionTokens);
        Assert.Equal(3, row.OwnerUserId);
        Assert.True(row.EstimatedCostUsd > 0);
    }

    [Fact]
    public async Task CompleteAsync_WhenDisabled_ReturnsDisabledWithoutCallingProvider()
    {
        var client = new FakeLlmClient();
        var service = Build(new LlmOptions { Enabled = false }, client);

        var result = await service.CompleteAsync(Req(), ownerUserId: null);

        Assert.Equal(LlmInsightStatus.Disabled, result.Status);
        Assert.Equal(0, client.Calls);
        Assert.Empty(_db.LlmUsageLogs);
    }

    [Fact]
    public async Task CompleteAsync_WhenModelEmpty_ReturnsNotConfiguredWithoutCallingProvider()
    {
        var client = new FakeLlmClient();
        var service = Build(new LlmOptions { Enabled = true, Model = "" }, client);

        var result = await service.CompleteAsync(Req(), ownerUserId: null);

        Assert.Equal(LlmInsightStatus.Error, result.Status);
        Assert.Equal(LlmErrorKind.NotConfigured.ToString(), result.ErrorKindLabel);
        Assert.Equal(0, client.Calls);
    }

    [Fact]
    public async Task CompleteAsync_BudgetExceeded_ReturnsBudgetExceededWithoutCallingProvider()
    {
        var client = new FakeLlmClient();
        var service = Build(EnabledOptions(monthlyBudget: 1.00m), client);
        _db.LlmUsageLogs.Add(new LlmUsageLog { Provider = "OpenAI", Model = "m", Operation = "x", EstimatedCostUsd = 1.00m, CreatedAtUtc = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        var result = await service.CompleteAsync(Req(), ownerUserId: null);

        Assert.Equal(LlmInsightStatus.BudgetExceeded, result.Status);
        Assert.Equal(0, client.Calls);
    }

    [Fact]
    public async Task CompleteAsync_LlmException_ReturnsFriendlyErrorAndRecordsFailedUsage()
    {
        var client = new FakeLlmClient { Throw = new LlmException("rate limited", LlmErrorKind.RateLimit) };
        var service = Build(EnabledOptions(), client);

        var result = await service.CompleteAsync(Req(), ownerUserId: null);

        Assert.Equal(LlmInsightStatus.Error, result.Status);
        Assert.Equal(LlmErrorKind.RateLimit.ToString(), result.ErrorKindLabel);
        Assert.Contains("rate-limited", result.FriendlyMessage, StringComparison.OrdinalIgnoreCase);

        var row = Assert.Single(_db.LlmUsageLogs);
        Assert.False(row.Success);
        Assert.Equal("RateLimit", row.ErrorKind);
    }

    [Fact]
    public async Task CompleteAsync_GenericException_ReturnsFriendlyError()
    {
        var client = new FakeLlmClient { Throw = new InvalidOperationException("boom") };
        var service = Build(EnabledOptions(), client);

        var result = await service.CompleteAsync(Req(), ownerUserId: null);

        Assert.Equal(LlmInsightStatus.Error, result.Status);
        Assert.Contains("Something went wrong", result.FriendlyMessage);
    }

    private sealed class FakeLlmClient : ILlmClient
    {
        public int Calls { get; private set; }
        public LlmCompletion Result { get; set; } = new("ok", 10, 5, "gpt-4o-mini");
        public Exception? Throw { get; set; }

        public Task<LlmCompletion> CompleteAsync(LlmRequest request, CancellationToken ct = default)
        {
            Calls++;
            if (Throw is not null) throw Throw;
            return Task.FromResult(Result);
        }
    }
}