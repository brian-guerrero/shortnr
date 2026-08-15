using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Web.Features.Insights;
using Shortnr.Web.Features.Insights.Llm;

namespace Shortnr.Web.Pages;

/// <summary>
/// AI link insights. PRD-006 lists pending tag suggestions the background analysis
/// produced for the current owner's (or active workspace's) links, with accept/reject
/// actions. PRD-023 adds an optional LLM layer: the four analysis operations
/// (analyze traffic, optimize campaign, draft social copy, suggest tags) run against
/// the configured AI provider through <see cref="LlmInsightService"/>, degrading
/// gracefully to the deterministic insights on any provider failure. Returns 404 when
/// the feature is disabled; mirrors the Dashboard/Activity access-control conventions.
/// </summary>
public class InsightsModel : PageModel, IStatusMessages
{
    private readonly AppDbContext _db;
    private readonly UserIdentityService _identity;
    private readonly IConfiguration _config;
    private readonly AiInsightsOptions _options;
    private readonly LlmInsightService _llmInsights;
    private readonly LlmInsightHistoryService _history;

    public InsightsModel(AppDbContext db, UserIdentityService identity, IConfiguration config,
        IOptions<AiInsightsOptions> options, LlmInsightService llmInsights, LlmInsightHistoryService history)
    {
        _db = db;
        _identity = identity;
        _config = config;
        _options = options.Value;
        _llmInsights = llmInsights;
        _history = history;
    }

    public List<InsightSuggestionRow> Suggestions { get; set; } = [];
    public List<LlmInsightRunRow> RecentRuns { get; set; } = [];
    /// <summary>A local-validation miss (bad short code/tag/URL) from the most recent "Ask AI"
    /// POST -- never reached the provider, so it's rendered once as a banner rather than
    /// persisted to <see cref="RecentRuns"/>. Null on every other request.</summary>
    public LlmInsightResult? TransientResult { get; set; }
    /// <summary>Backs the &lt;datalist&gt; typeahead on the short-code inputs (Analyze/Draft social copy).</summary>
    public List<string> ShortCodeOptions { get; set; } = [];
    /// <summary>Backs the &lt;datalist&gt; typeahead on the campaign-tag input (Optimize campaign).</summary>
    public List<string> CampaignTagOptions { get; set; } = [];
    public string? StatusMessage { get; set; }
    public string? ErrorMessage { get; set; }
    public int AnalysisIntervalHours => _options.AnalysisIntervalHours;
    public int MinClicksForAnalysis => _options.MinClicksForAnalysis;
    public bool LlmEnabled => _config.GetValue<bool>("AiInsights:Llm:Enabled", defaultValue: false);

    private bool IsEnabled => _config.GetValue<bool>("AiInsights:Enabled", defaultValue: false);

    public async Task<IActionResult> OnGet()
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        if (Request.Headers["HX-Request"].Count > 0)
            return Partial("Shared/_InsightsList", this);

        Suggestions = await LoadSuggestionsAsync();
        if (LlmEnabled)
        {
            RecentRuns = await _history.RecentAsync(await _identity.ResolveOwnerUserIdAsync(User));
            await LoadAskAiOptionsAsync();
        }
        return Page();
    }

    /// <summary>
    /// Runs the analysis pass immediately instead of waiting for the next
    /// <see cref="AiInsightsHostedService"/> tick. Builds <see cref="AiInsightsService"/>
    /// directly from the request-scoped <see cref="AppDbContext"/> and options this page
    /// already holds, rather than resolving it from DI — that registration only exists
    /// when <c>AiInsights:Enabled</c> was true at host build time, which can lag a
    /// runtime config read (e.g. under a test factory that overrides config after
    /// startup); constructing it here works regardless.
    /// </summary>
    public async Task<IActionResult> OnPostRunNow()
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        var insightsService = new AiInsightsService(_db, Options.Create(_options));
        var created = await insightsService.RunAnalysisAsync();

        return await ListPartialAsync(status: created == 0
            ? "Analysis complete — no new suggestions."
            : $"Analysis complete — {created} new suggestion{(created == 1 ? "" : "s")} found.");
    }

    // -------------------------------------------------------------------------
    // PRD-023 LLM analysis operations
    // -------------------------------------------------------------------------

    /// <summary>POST /insights?handler=Analyze — explain a link's traffic patterns.</summary>
    public Task<IActionResult> OnPostAnalyzeAsync(string? code) =>
        RunLlmAsync(LlmOperation.AnalyzeTraffic, code ?? "", async (ownerUserId, workspaceId) =>
        {
            if (string.IsNullOrWhiteSpace(code))
                return LlmResultNotFound("Enter a short code first.");

            var summary = await BuildScopedLinkSummaryAsync(code, ownerUserId, workspaceId);
            if (summary is null)
                return LlmResultNotFound($"No link with code '{code}' was found in your account.");

            return await _llmInsights.CompleteAsync(
                LlmPromptFactory.AnalyzeTraffic(code, summary, _llmInsights.Model), ownerUserId);
        });

    /// <summary>POST /insights?handler=OptimizeCampaign — suggest campaign optimizations.</summary>
    public Task<IActionResult> OnPostOptimizeCampaignAsync(string? tag) =>
        RunLlmAsync(LlmOperation.OptimizeCampaign, tag ?? "", async (ownerUserId, workspaceId) =>
        {
            if (string.IsNullOrWhiteSpace(tag))
                return LlmResultNotFound("Enter a campaign tag first.");

            var summary = await BuildCampaignSummaryAsync(tag, ownerUserId, workspaceId);
            if (summary is null)
                return LlmResultNotFound($"No links tagged '{tag}' were found in your account.");

            return await _llmInsights.CompleteAsync(
                LlmPromptFactory.OptimizeCampaign(tag, summary, _llmInsights.Model), ownerUserId);
        });

    /// <summary>POST /insights?handler=DraftSocialCopy — draft social copy for a link.</summary>
    public Task<IActionResult> OnPostDraftSocialCopyAsync(string? code) =>
        RunLlmAsync(LlmOperation.DraftSocialCopy, code ?? "", async (ownerUserId, workspaceId) =>
        {
            if (string.IsNullOrWhiteSpace(code))
                return LlmResultNotFound("Enter a short code first.");

            var summary = await BuildScopedLinkSummaryAsync(code, ownerUserId, workspaceId);
            if (summary is null)
                return LlmResultNotFound($"No link with code '{code}' was found in your account.");

            return await _llmInsights.CompleteAsync(
                LlmPromptFactory.DraftSocialCopy(code, summary, _llmInsights.Model), ownerUserId);
        });

    /// <summary>POST /insights?handler=SuggestTags — suggest tags for a new destination URL.</summary>
    public Task<IActionResult> OnPostSuggestTagsAsync(string? url) =>
        RunLlmAsync(LlmOperation.SuggestTags, url ?? "", async (ownerUserId, workspaceId) =>
        {
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out _))
                return LlmResultNotFound("Enter a full URL (starting with http:// or https://) first.");

            return await _llmInsights.CompleteAsync(
                LlmPromptFactory.SuggestTags(url, _llmInsights.Model), ownerUserId);
        });

    // -------------------------------------------------------------------------
    // PRD-006 accept / dismiss
    // -------------------------------------------------------------------------

    public async Task<IActionResult> OnPostAccept(long id)
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        var suggestion = await FindScopedSuggestionAsync(id);
        if (suggestion is null)
            return await ListPartialAsync(error: "Suggestion not found.");

        if (suggestion.Status != TagSuggestionStatus.Pending)
            return await ListPartialAsync(error: "Suggestion has already been reviewed.");

        var tagName = suggestion.SuggestedTag;
        var alreadyApplied = await _db.ShortenedUrlTags
            .AnyAsync(t => t.ShortenedUrlId == suggestion.ShortenedUrlId && t.Name == tagName);

        if (!alreadyApplied)
        {
            _db.ShortenedUrlTags.Add(new ShortenedUrlTag
            {
                ShortenedUrlId = suggestion.ShortenedUrlId,
                Name = tagName,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        suggestion.Status = TagSuggestionStatus.Accepted;
        await _db.SaveChangesAsync();

        return await ListPartialAsync(status: $"Tag '{tagName}' applied to the link.");
    }

    public async Task<IActionResult> OnPostDismiss(long id)
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        var suggestion = await FindScopedSuggestionAsync(id);
        if (suggestion is null)
            return await ListPartialAsync(error: "Suggestion not found.");

        if (suggestion.Status != TagSuggestionStatus.Pending)
            return await ListPartialAsync(error: "Suggestion has already been reviewed.");

        var tagName = suggestion.SuggestedTag;
        suggestion.Status = TagSuggestionStatus.Dismissed;
        await _db.SaveChangesAsync();

        return await ListPartialAsync(status: $"Tag '{tagName}' dismissed.");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private IActionResult? EnforceAccess()
    {
        if (!IsEnabled)
            return NotFound();

        if (_identity.IsAuthEnabled && User.Identity?.IsAuthenticated != true)
            return Request.Headers["HX-Request"].Count > 0
                ? Unauthorized()
                : RedirectToPage("/Index");

        return null;
    }

    /// <summary>
    /// Shared wrapper for the four "Ask AI" handlers: enforce access, run the operation, and
    /// -- unless it was a local NotFound (bad short code/tag/URL, never reached the provider) --
    /// persist the outcome to <see cref="LlmInsightHistoryService"/> so it survives a refresh.
    /// Always re-renders the full history list (not just the new result) so the swapped-in
    /// content matches what a GET would show.
    /// </summary>
    private async Task<IActionResult> RunLlmAsync(LlmOperation operation, string inputSummary, Func<long?, long?, Task<LlmInsightResult>> action)
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        var ownerUserId = await _identity.ResolveOwnerUserIdAsync(User);
        var workspace = await _identity.ResolveActiveWorkspaceContextAsync(User);

        var result = await action(ownerUserId, workspace?.WorkspaceId);

        if (result.Status == LlmInsightStatus.NotFound)
            TransientResult = result;
        else
            await _history.RecordAsync(ownerUserId, operation, inputSummary, result);

        RecentRuns = await _history.RecentAsync(ownerUserId);
        return Partial("Shared/_LlmInsightHistory", this);
    }

    private static LlmInsightResult LlmResultNotFound(string message) => new()
    {
        Status = LlmInsightStatus.NotFound,
        FriendlyMessage = message
    };

    private IQueryable<ShortenedUrl> ScopedLinks(long? ownerUserId, long? workspaceId)
    {
        var query = _db.ShortenedUrls.AsNoTracking().AsQueryable();
        if (workspaceId is not null)
            query = query.Where(l => l.WorkspaceId == workspaceId);
        else if (ownerUserId is not null)
            query = query.Where(l => l.OwnerUserId == ownerUserId);
        return query;
    }

    private async Task<string?> BuildScopedLinkSummaryAsync(string code, long? ownerUserId, long? workspaceId)
    {
        var link = await ScopedLinks(ownerUserId, workspaceId).FirstOrDefaultAsync(l => l.ShortCode == code);
        if (link is null)
            return null;
        return await BuildLinkSummaryAsync(link);
    }

    private async Task<string> BuildLinkSummaryAsync(ShortenedUrl link)
    {
        var since = DateTime.UtcNow.AddDays(-30);
        var clicks = await _db.ClickEvents.AsNoTracking()
            .Where(c => c.ShortenedUrlId == link.Id && c.ClickedAtUtc >= since)
            .OrderByDescending(c => c.ClickedAtUtc)
            .Take(500)
            .Select(c => new ClickDatum(c.Referer, c.ClickedAtUtc))
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine($"Destination: {link.LongUrl}");
        sb.AppendLine($"Clicks, all time: {link.ClickCount}");
        sb.AppendLine($"Clicks in the last 30 days (up to 500 sampled): {clicks.Count}");

        var byDay = clicks
            .GroupBy(c => c.ClickedAtUtc.Date)
            .OrderByDescending(g => g.Key)
            .Select(g => $"{g.Key:yyyy-MM-dd}: {g.Count()}");
        sb.AppendLine($"Clicks per day: {(byDay.Any() ? string.Join("; ", byDay) : "none")}");

        var byReferrer = clicks
            .GroupBy(c => InsightHeuristics.NormalizeReferrerHost(c.Referer))
            .Where(g => !string.IsNullOrEmpty(g.Key))
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g => $"{g.Key}: {g.Count()}");
        sb.AppendLine($"Top referrers: {(byReferrer.Any() ? string.Join("; ", byReferrer) : "none")}");

        return sb.ToString();
    }

    private async Task<string?> BuildCampaignSummaryAsync(string tag, long? ownerUserId, long? workspaceId)
    {
        var links = await ScopedLinks(ownerUserId, workspaceId)
            .Where(l => l.Tags.Any(t => t.Name == tag))
            .OrderByDescending(l => l.ClickCount)
            .Take(20)
            .Select(l => new { l.Id, l.ShortCode, l.ClickCount })
            .ToListAsync();

        if (links.Count == 0)
            return null;

        var since = DateTime.UtcNow.AddDays(-30);
        var linkIds = links.Select(l => l.Id).ToList();
        var referrersByLink = await _db.ClickEvents.AsNoTracking()
            .Where(c => linkIds.Contains(c.ShortenedUrlId) && c.ClickedAtUtc >= since)
            .Select(c => new { c.ShortenedUrlId, c.Referer })
            .ToListAsync();

        var sb = new StringBuilder();
        foreach (var link in links)
        {
            var top = referrersByLink
                .Where(c => c.ShortenedUrlId == link.Id)
                .GroupBy(c => InsightHeuristics.NormalizeReferrerHost(c.Referer))
                .Where(g => !string.IsNullOrEmpty(g.Key))
                .OrderByDescending(g => g.Count())
                .FirstOrDefault();

            sb.AppendLine($"{link.ShortCode} | all-time clicks: {link.ClickCount} | top referrer: {top?.Key ?? "direct / unknown"} ({top?.Count() ?? 0} in window)");
        }
        return sb.ToString();
    }

    private async Task<List<InsightSuggestionRow>> LoadSuggestionsAsync()
    {
        var ownerUserId = await _identity.ResolveOwnerUserIdAsync(User);
        var workspace = await _identity.ResolveActiveWorkspaceContextAsync(User);
        var workspaceId = workspace?.WorkspaceId;

        var query = _db.TagSuggestions
            .AsNoTracking()
            .Where(s => s.Status == TagSuggestionStatus.Pending)
            .AsQueryable();

        if (workspaceId is not null)
            query = query.Where(s => s.ShortenedUrl.WorkspaceId == workspaceId);
        else if (ownerUserId is not null)
            query = query.Where(s => s.ShortenedUrl.OwnerUserId == ownerUserId);

        return await query
            .OrderByDescending(s => s.CreatedAtUtc)
            .ThenByDescending(s => s.Id)
            .Take(100)
            .Select(s => new InsightSuggestionRow
            {
                Id = s.Id,
                ShortCode = s.ShortenedUrl.ShortCode,
                SuggestedTag = s.SuggestedTag,
                Source = s.Source,
                ClickCount = s.ClickCount,
                FirstObservedUtc = s.FirstObservedUtc,
                CreatedAtUtc = s.CreatedAtUtc
            })
            .ToListAsync();
    }

    /// <summary>Populates the &lt;datalist&gt; typeahead options for the "Ask AI" short-code and
    /// campaign-tag inputs, scoped the same way as everything else on this page.</summary>
    private async Task LoadAskAiOptionsAsync()
    {
        var ownerUserId = await _identity.ResolveOwnerUserIdAsync(User);
        var workspace = await _identity.ResolveActiveWorkspaceContextAsync(User);
        var workspaceId = workspace?.WorkspaceId;

        var activeLinks = ScopedLinks(ownerUserId, workspaceId).Where(l => l.ArchivedAtUtc == null);

        ShortCodeOptions = await activeLinks
            .OrderByDescending(l => l.ClickCount)
            .Select(l => l.ShortCode)
            .Take(50)
            .ToListAsync();

        CampaignTagOptions = await activeLinks
            .SelectMany(l => l.Tags)
            .Select(t => t.Name)
            .Distinct()
            .OrderBy(t => t)
            .Take(50)
            .ToListAsync();
    }

    private async Task<TagSuggestion?> FindScopedSuggestionAsync(long id)
    {
        var ownerUserId = await _identity.ResolveOwnerUserIdAsync(User);
        var workspace = await _identity.ResolveActiveWorkspaceContextAsync(User);
        var workspaceId = workspace?.WorkspaceId;

        var query = _db.TagSuggestions.AsQueryable();
        if (workspaceId is not null)
            query = query.Where(s => s.ShortenedUrl.WorkspaceId == workspaceId);
        else if (ownerUserId is not null)
            query = query.Where(s => s.ShortenedUrl.OwnerUserId == ownerUserId);

        return await query.FirstOrDefaultAsync(s => s.Id == id);
    }

    private async Task<IActionResult> ListPartialAsync(string? status = null, string? error = null)
    {
        StatusMessage = status;
        ErrorMessage = error;
        Suggestions = await LoadSuggestionsAsync();
        return Partial("Shared/_InsightsList", this);
    }
}