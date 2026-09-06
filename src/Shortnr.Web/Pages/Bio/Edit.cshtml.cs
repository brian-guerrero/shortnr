using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;

namespace Shortnr.Web.Pages.Bio;

public class EditModel(AppDbContext db, UserIdentityService identity, ShortenRateLimiter shortenLimiter,
    ShortLinkService links) : AuthorizedPageModel(identity)
{
    public BioPage? BioPage { get; set; }
    public List<ShortenedUrl> OwnerLinks { get; set; } = [];
    public ActiveWorkspaceContext? Workspace { get; set; }

    public async Task<IActionResult> OnGet()
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        Workspace = await identity.ResolveActiveWorkspaceContextAsync(User);
        await LoadEditorAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostCreate()
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        if (!await shortenLimiter.TryAcquireAsync(HttpContext))
            return new StatusCodeResult(StatusCodes.Status429TooManyRequests);

        var ownerUserId = await identity.ResolveOwnerUserIdAsync(User);
        if (ownerUserId is null)
            return await EditorPartialAsync(error: "A bio page requires a signed-in account. Try again in a moment.");

        if (await db.BioPages.AnyAsync(b => b.OwnerUserId == ownerUserId))
            return await EditorPartialAsync(error: "You already have a bio page.");

        var slug = (Request.Form["slug"].FirstOrDefault() ?? "").Trim();
        if (!ShortLinkCodes.IsValidSlug(slug))
            return await EditorPartialAsync(error: "Bio page code must be 1–64 characters: letters, digits, '-' and '_', starting with a letter or digit.");

        if (await db.BioPages.AnyAsync(b => b.Slug == slug))
            return await EditorPartialAsync(error: $"The bio page code '{slug}' is already taken.");

        var displayName = (Request.Form["displayName"].FirstOrDefault() ?? "").Trim();
        var theme = NormaliseTheme(Request.Form["theme"].FirstOrDefault());

        db.BioPages.Add(new BioPage
        {
            OwnerUserId = ownerUserId.Value,
            Slug = slug,
            DisplayName = displayName.Length > 0 ? displayName : slug,
            AvatarUrl = CleanUrl(Request.Form["avatarUrl"].FirstOrDefault()),
            BioText = (Request.Form["bioText"].FirstOrDefault() ?? "").Trim(),
            Theme = theme,
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        await LoadEditorAsync();
        StatusMessage = $"Bio page created at /bio/{slug}.";
        return Partial("Shared/_BioEditor", this);
    }

    public async Task<IActionResult> OnPostUpdate()
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        var bioPage = await FindOwnedPageAsync();
        if (bioPage is null)
            return await EditorPartialAsync(error: "Create your bio page first.");

        var displayName = (Request.Form["displayName"].FirstOrDefault() ?? "").Trim();
        bioPage.DisplayName = displayName.Length > 0 ? displayName : bioPage.DisplayName;
        bioPage.AvatarUrl = CleanUrl(Request.Form["avatarUrl"].FirstOrDefault());
        bioPage.BioText = (Request.Form["bioText"].FirstOrDefault() ?? "").Trim();
        bioPage.Theme = NormaliseTheme(Request.Form["theme"].FirstOrDefault());
        await db.SaveChangesAsync();

        await LoadEditorAsync();
        StatusMessage = "Bio page updated.";
        return Partial("Shared/_BioEditor", this);
    }

    public async Task<IActionResult> OnPostAddLink()
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        var bioPage = await FindOwnedPageAsync();
        if (bioPage is null)
            return await EditorPartialAsync(error: "Create your bio page first.");

        Workspace = await identity.ResolveActiveWorkspaceContextAsync(User);
        var workspaceId = Workspace?.WorkspaceId;
        var ownerUserId = await identity.ResolveOwnerUserIdAsync(User);
        var linkId = long.TryParse(Request.Form["linkId"].FirstOrDefault(), out var id) ? id : (long?)null;
        var newUrl = (Request.Form["newUrl"].FirstOrDefault() ?? "").Trim();
        var newSlug = (Request.Form["newSlug"].FirstOrDefault() ?? "").Trim();

        ShortenedUrl? link;
        if (linkId is not null)
        {
            link = await db.ShortenedUrls
                .Where(l => l.Id == linkId.Value)
                .ScopeToOwnerIncludingWorkspace(ownerUserId, workspaceId)
                .FirstOrDefaultAsync();
            if (link is null)
                return await EditorPartialAsync(error: "Link not found.");
        }
        else if (newUrl.Length > 0)
        {
            var created = await CreateLinkAsync(newUrl, newSlug, ownerUserId, workspaceId);
            if (created.Error is not null)
                return await EditorPartialAsync(error: created.Error);
            link = created.Link;
        }
        else
        {
            return await EditorPartialAsync(error: "Choose an existing link or paste a URL to add one.");
        }

        if (link is null)
            return await EditorPartialAsync(error: "Could not add that link.");

        if (await db.BioPageLinks.AnyAsync(b => b.BioPageId == bioPage.Id && b.ShortenedUrlId == link.Id))
            return await EditorPartialAsync(error: "That link is already on your bio page.");

        var maxOrder = await db.BioPageLinks
            .Where(b => b.BioPageId == bioPage.Id)
            .Select(b => (int?)b.SortOrder)
            .MaxAsync() ?? -1;

        var title = (Request.Form["title"].FirstOrDefault() ?? "").Trim();
        db.BioPageLinks.Add(new BioPageLink
        {
            BioPageId = bioPage.Id,
            ShortenedUrlId = link.Id,
            Title = title.Length > 0 ? title : link.ShortCode,
            SortOrder = maxOrder + 1,
            IsVisible = true
        });
        await db.SaveChangesAsync();

        await LoadEditorAsync();
        StatusMessage = "Link added to your bio page.";
        return Partial("Shared/_BioEditor", this);
    }

    public async Task<IActionResult> OnPostRemoveLink(long id)
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        var bioPage = await FindOwnedPageAsync();
        if (bioPage is null)
            return await EditorPartialAsync(error: "Create your bio page first.");

        var entry = await db.BioPageLinks.FirstOrDefaultAsync(b => b.Id == id && b.BioPageId == bioPage.Id);
        if (entry is null)
            return await EditorPartialAsync(error: "Link not found on your bio page.");

        db.BioPageLinks.Remove(entry);
        await db.SaveChangesAsync();

        await LoadEditorAsync();
        StatusMessage = "Link removed from your bio page.";
        return Partial("Shared/_BioEditor", this);
    }

    public async Task<IActionResult> OnPostMoveLink(long id, string direction)
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        var bioPage = await FindOwnedPageAsync();
        if (bioPage is null)
            return await EditorPartialAsync(error: "Create your bio page first.");

        var entries = await db.BioPageLinks
            .Where(b => b.BioPageId == bioPage.Id)
            .OrderBy(b => b.SortOrder)
            .ThenBy(b => b.Id)
            .ToListAsync();

        var index = entries.FindIndex(e => e.Id == id);
        if (index < 0)
            return await EditorPartialAsync(error: "Link not found on your bio page.");

        var swapIndex = direction == "up" ? index - 1 : index + 1;
        if (swapIndex < 0 || swapIndex >= entries.Count)
            return await EditorPartialAsync(error: "Link is already at the edge.");

        (entries[index].SortOrder, entries[swapIndex].SortOrder) =
            (entries[swapIndex].SortOrder, entries[index].SortOrder);
        await db.SaveChangesAsync();

        await LoadEditorAsync();
        return Partial("Shared/_BioEditor", this);
    }

    public async Task<IActionResult> OnPostToggleLink(long id)
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        var bioPage = await FindOwnedPageAsync();
        if (bioPage is null)
            return await EditorPartialAsync(error: "Create your bio page first.");

        var entry = await db.BioPageLinks.FirstOrDefaultAsync(b => b.Id == id && b.BioPageId == bioPage.Id);
        if (entry is null)
            return await EditorPartialAsync(error: "Link not found on your bio page.");

        entry.IsVisible = !entry.IsVisible;
        await db.SaveChangesAsync();

        await LoadEditorAsync();
        return Partial("Shared/_BioEditor", this);
    }

    private async Task<IActionResult> EditorPartialAsync(string? status = null, string? error = null)
    {
        StatusMessage = status;
        ErrorMessage = error;
        await LoadEditorAsync();
        return Partial("Shared/_BioEditor", this);
    }

    private async Task LoadEditorAsync()
    {
        var ownerUserId = await identity.ResolveOwnerUserIdAsync(User);
        Workspace = await identity.ResolveActiveWorkspaceContextAsync(User);
        var workspaceId = Workspace?.WorkspaceId;

        BioPage = ownerUserId is null
            ? null
            : await db.BioPages
                .Include(b => b.Links)
                .ThenInclude(l => l.ShortenedUrl)
                .ThenInclude(s => s!.Domain)
                .FirstOrDefaultAsync(b => b.OwnerUserId == ownerUserId);

        if (BioPage is not null)
        {
            BioPage.Links = BioPage.Links
                .OrderBy(l => l.SortOrder)
                .ThenBy(l => l.Id)
                .ToList();
        }

        if (ownerUserId is null)
        {
            OwnerLinks = [];
        }
        else
        {
            OwnerLinks = await db.ShortenedUrls
                .Include(l => l.Domain)
                .ScopeToOwnerIncludingWorkspace(ownerUserId, workspaceId)
                .OrderByDescending(l => l.CreatedAtUtc)
                .Take(100)
                .ToListAsync();
        }
    }

    private async Task<BioPage?> FindOwnedPageAsync()
    {
        var ownerUserId = await identity.ResolveOwnerUserIdAsync(User);
        return ownerUserId is null
            ? null
            : await db.BioPages.FirstOrDefaultAsync(b => b.OwnerUserId == ownerUserId);
    }

    /// <summary>
    /// Creates a new <see cref="ShortenedUrl"/> on the instance host, mirroring the
    /// Index page's creation rules (valid http(s) URL, slug rules via
    /// <see cref="ShortLinkCodes"/>). Returns an error message on failure.
    /// </summary>
    private async Task<(ShortenedUrl? Link, string? Error)> CreateLinkAsync(string url, string slug, long? ownerUserId, long? workspaceId)
    {
        // Bio links always live on the instance host, so no domain is resolved here.
        var outcome = await links.CreateAsync(new NewLinkRequest(url, slug, DomainId: null, ownerUserId, workspaceId));

        return outcome switch
        {
            CreateLinkOutcome.Created created => (created.Link, null),
            CreateLinkOutcome.InvalidUrl => (null, "Enter a valid http(s) URL."),
            CreateLinkOutcome.InvalidSlug => (null, "Custom code must be 1–64 characters: letters, digits, '-' and '_', starting with a letter or digit."),
            CreateLinkOutcome.SlugTaken taken => (null, $"The custom code '{taken.Slug}' is already taken."),
            _ => (null, "Could not add that link.")
        };
    }


    private static string NormaliseTheme(string? theme) =>
        BioThemes.IsValid(theme) ? theme! : "default";

    private static string? CleanUrl(string? url)
    {
        url = url?.Trim();
        return string.IsNullOrEmpty(url) ? null : url;
    }
}
