using Microsoft.EntityFrameworkCore;
using Shortnr.Data;

namespace Shortnr.Tests.Unit.Services;

public class LlmInsightHistoryServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly LlmInsightHistoryService _history;

    public LlmInsightHistoryServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _history = new LlmInsightHistoryService(_db);
    }

    public void Dispose() => _db.Dispose();

    private static LlmInsightResult Success(string content) => new() { Status = LlmInsightStatus.Success, Content = content };
    private static LlmInsightResult Failure(string message) => new() { Status = LlmInsightStatus.Error, FriendlyMessage = message, ErrorKindLabel = "RateLimit" };

    [Fact]
    public async Task RecordAsync_Success_PersistsContentWithoutFriendlyMessage()
    {
        await _history.RecordAsync(7, LlmOperation.AnalyzeTraffic, "aaa111", Success("Traffic spiked from Reddit."));

        var row = Assert.Single(_db.LlmInsightRuns);
        Assert.Equal(7, row.OwnerUserId);
        Assert.Equal("AnalyzeTraffic", row.Operation);
        Assert.Equal("aaa111", row.InputSummary);
        Assert.True(row.Success);
        Assert.Equal("Traffic spiked from Reddit.", row.Content);
        Assert.Null(row.FriendlyMessage);
    }

    [Fact]
    public async Task RecordAsync_Failure_PersistsFriendlyMessageWithoutContent()
    {
        await _history.RecordAsync(7, LlmOperation.SuggestTags, "https://example.com", Failure("The AI provider is rate-limited right now."));

        var row = Assert.Single(_db.LlmInsightRuns);
        Assert.False(row.Success);
        Assert.Null(row.Content);
        Assert.Equal("The AI provider is rate-limited right now.", row.FriendlyMessage);
    }

    [Fact]
    public async Task RecentAsync_ReturnsNewestFirst_ScopedToOwner()
    {
        await _history.RecordAsync(1, LlmOperation.AnalyzeTraffic, "first", Success("one"));
        await _history.RecordAsync(1, LlmOperation.SuggestTags, "second", Success("two"));
        await _history.RecordAsync(2, LlmOperation.AnalyzeTraffic, "other-owner", Success("three"));

        var runs = await _history.RecentAsync(1);

        Assert.Equal(2, runs.Count);
        Assert.Equal("second", runs[0].InputSummary);
        Assert.Equal("first", runs[1].InputSummary);
        Assert.Equal(LlmOperation.SuggestTags, runs[0].Operation);
    }

    [Fact]
    public async Task RecentAsync_RespectsTakeLimit()
    {
        for (var i = 0; i < 5; i++)
            await _history.RecordAsync(1, LlmOperation.AnalyzeTraffic, $"run-{i}", Success("ok"));

        var runs = await _history.RecentAsync(1, take: 2);

        Assert.Equal(2, runs.Count);
    }

    [Fact]
    public async Task RecentAsync_NullOwner_ScopesToOtherNullOwnerRunsOnly()
    {
        await _history.RecordAsync(null, LlmOperation.AnalyzeTraffic, "anon", Success("anon result"));
        await _history.RecordAsync(3, LlmOperation.AnalyzeTraffic, "owned", Success("owned result"));

        var runs = await _history.RecentAsync(null);

        var row = Assert.Single(runs);
        Assert.Equal("anon", row.InputSummary);
    }
}
