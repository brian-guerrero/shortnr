using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shortnr.Data;
using Shortnr.Data.Entities;

namespace Shortnr.Tests.Unit.Services;

public class AiInsightsServiceTests : IDisposable
{
    private readonly AppDbContext _db;

    public AiInsightsServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    private AiInsightsService BuildService() =>
        new(_db, Options.Create(new AiInsightsOptions
        {
            Enabled = true,
            AnalysisIntervalHours = 24,
            MinClicksForAnalysis = 10
        }));

    private static ShortenedUrl Link(long clickCount = 10, string? longUrl = null) => new()
    {
        LongUrl = longUrl ?? "https://example.com/landing",
        ShortCode = "abc123",
        ClickCount = clickCount,
        CreatedAtUtc = DateTime.UtcNow
    };

    private static ClickEvent Click(ShortenedUrl link, string referer, DateTime? at = null) => new()
    {
        ShortenedUrlId = link.Id,
        ShortenedUrl = link,
        Referer = referer,
        UserAgent = "test",
        IpAddress = "1.2.3.4",
        ClickedAtUtc = at ?? DateTime.UtcNow
    };

    [Fact]
    public async Task RunAnalysis_EligibleLinkWithReferrerCluster_CreatesSuggestion()
    {
        var link = Link(clickCount: 10);
        _db.ShortenedUrls.Add(link);
        await _db.SaveChangesAsync();

        for (var i = 0; i < 7; i++)
            _db.ClickEvents.Add(Click(link, "https://www.facebook.com/post/1"));
        for (var i = 0; i < 3; i++)
            _db.ClickEvents.Add(Click(link, "https://news.example.com/x"));
        await _db.SaveChangesAsync();

        var service = BuildService();
        var created = await service.RunAnalysisAsync();

        Assert.Equal(1, created);
        var suggestion = Assert.Single(_db.TagSuggestions);
        Assert.Equal(link.Id, suggestion.ShortenedUrlId);
        Assert.Equal("facebook.com", suggestion.SuggestedTag);
        Assert.Equal(TagSuggestionSource.ReferrerDomainCluster, suggestion.Source);
        Assert.Equal(TagSuggestionStatus.Pending, suggestion.Status);
    }

    [Fact]
    public async Task RunAnalysis_LinkBelowMinClicks_IsSkipped()
    {
        var link = Link(clickCount: 9);
        _db.ShortenedUrls.Add(link);
        await _db.SaveChangesAsync();

        for (var i = 0; i < 9; i++)
            _db.ClickEvents.Add(Click(link, "https://www.facebook.com/post/1"));
        await _db.SaveChangesAsync();

        var service = BuildService();
        var created = await service.RunAnalysisAsync();

        Assert.Equal(0, created);
        Assert.Empty(_db.TagSuggestions);
    }

    [Fact]
    public async Task RunAnalysis_NoClicksInWindow_NoSuggestion()
    {
        var link = Link(clickCount: 10);
        _db.ShortenedUrls.Add(link);
        await _db.SaveChangesAsync();

        // Clicks are older than the 24h analysis window; the link's lifetime
        // ClickCount still makes it a candidate, but the window has no traffic.
        for (var i = 0; i < 10; i++)
            _db.ClickEvents.Add(Click(link, "https://www.facebook.com/post/1", DateTime.UtcNow.AddHours(-30)));
        await _db.SaveChangesAsync();

        var service = BuildService();
        var created = await service.RunAnalysisAsync();

        Assert.Equal(0, created);
        Assert.Empty(_db.TagSuggestions);
    }

    [Fact]
    public async Task RunAnalysis_ExistingTag_IsNotResuggested()
    {
        var link = Link(clickCount: 10);
        _db.ShortenedUrls.Add(link);
        await _db.SaveChangesAsync();

        _db.TagSuggestions.Add(new TagSuggestion
        {
            ShortenedUrlId = link.Id,
            SuggestedTag = "facebook.com",
            Source = TagSuggestionSource.ReferrerDomainCluster,
            Status = TagSuggestionStatus.Dismissed,
            ClickCount = 7,
            FirstObservedUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        for (var i = 0; i < 7; i++)
            _db.ClickEvents.Add(Click(link, "https://www.facebook.com/post/1"));
        await _db.SaveChangesAsync();

        var service = BuildService();
        var created = await service.RunAnalysisAsync();

        Assert.Equal(0, created);
        Assert.Single(_db.TagSuggestions);
    }

    [Fact]
    public async Task RunAnalysis_UtmOnlyLink_CreatesUtmSuggestion()
    {
        var link = Link(clickCount: 10, longUrl: "https://example.com/landing?utm_source=newsletter");
        _db.ShortenedUrls.Add(link);
        await _db.SaveChangesAsync();

        for (var i = 0; i < 10; i++)
            _db.ClickEvents.Add(Click(link, ""));
        await _db.SaveChangesAsync();

        var service = BuildService();
        var created = await service.RunAnalysisAsync();

        Assert.Equal(1, created);
        var suggestion = Assert.Single(_db.TagSuggestions);
        Assert.Equal("newsletter", suggestion.SuggestedTag);
        Assert.Equal(TagSuggestionSource.UtmExtraction, suggestion.Source);
    }
}
