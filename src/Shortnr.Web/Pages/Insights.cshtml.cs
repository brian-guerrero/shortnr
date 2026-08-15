using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Web.Features.Insights;

namespace Shortnr.Web.Pages;

/// <summary>
/// AI link insights (PRD-006): lists pending tag suggestions the background
/// analysis produced for the current owner's (or active workspace's) links, with
/// accept/reject actions. Returns 404 when the feature is disabled; mirrors the
/// Dashboard/Activity access-control conventions.
/// </summary>
public class InsightsModel : PageModel, IStatusMessages
{
    private readonly AppDbContext _db;
    private readonly UserIdentityService _identity;
    private readonly IConfiguration _config;
    private readonly AiInsightsOptions _options;

    public InsightsModel(AppDbContext db, UserIdentityService identity, IConfiguration config,
        IOptions<AiInsightsOptions> options)
    {
        _db = db;
        _identity = identity;
        _config = config;
        _options = options.Value;
    }

    public List<InsightSuggestionRow> Suggestions { get; set; } = [];
    public string? StatusMessage { get; set; }
    public string? ErrorMessage { get; set; }
    public int AnalysisIntervalHours => _options.AnalysisIntervalHours;
    public int MinClicksForAnalysis => _options.MinClicksForAnalysis;

    private bool IsEnabled => _config.GetValue<bool>("AiInsights:Enabled", defaultValue: false);

    public async Task<IActionResult> OnGet()
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        if (Request.Headers["HX-Request"].Count > 0)
            return Partial("Shared/_InsightsList", this);

        Suggestions = await LoadSuggestionsAsync();
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
