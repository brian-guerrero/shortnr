using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Web.Services;

namespace Shortnr.Web.Pages.Bio;

public class EditModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly UserIdentityService _identity;
    private readonly ShortenRateLimiter _shortenLimiter;

    public BioPage? BioPage { get; set; }
    public List<ShortenedUrl> OwnerLinks { get; set; } = [];
    public string? StatusMessage { get; set; }
    public string? ErrorMessage { get; set; }
    public bool IsHtmxRequest { get; set; }

    public EditModel(AppDbContext db, UserIdentityService identity, ShortenRateLimiter shortenLimiter)
    {
        _db = db;
        _identity = identity;
        _shortenLimiter = shortenLimiter;
    }

    public async Task<IActionResult> OnGet()
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        IsHtmxRequest = Request.Headers["HX-Request"].Count > 0;
        await LoadEditorAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostCreate()
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        if (!await _shortenLimiter.TryAcquireAsync(HttpContext))
            return new StatusCodeResult(StatusCodes.Status429TooManyRequests);

        var ownerUserId = await _identity.ResolveOwnerUserIdAsync(User);
        if (ownerUserId is null)
            return await EditorPartialAsync(error: "A bio page requires a signed-in account. Try again in a moment.");

        if (await _db.BioPages.AnyAsync(b => b.OwnerUserId == ownerUserId))
            return await EditorPartialAsync(error: "You already have a bio page.");

        var slug = (Request.Form["slug"].FirstOrDefault() ?? "").Trim();
        if (!ShortLinkCodes.IsValidSlug(slug))
            return await EditorPartialAsync(error: "Bio page code must be 1–64 characters: letters, digits, '-' and '_', starting with a letter or digit.");

        if (await _db.BioPages.AnyAsync(b => b.Slug == slug))
            return await EditorPartialAsync(error: $"The bio page code '{slug}' is already taken.");

        var displayName = (Request.Form["displayName"].FirstOrDefault() ?? "").Trim();
        var theme = NormaliseTheme(Request.Form["theme"].FirstOrDefault());

        _db.BioPages.Add(new BioPage
        {
            OwnerUserId = ownerUserId.Value,
            Slug = slug,
            DisplayName = displayName.Length > 0 ? displayName : slug,
            AvatarUrl = CleanUrl(Request.Form["avatarUrl"].FirstOrDefault()),
            BioText = (Request.Form["bioText"].FirstOrDefault() ?? "").Trim(),
            Theme = theme,
            CreatedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

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
        await _db.SaveChangesAsync();

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

        var ownerUserId = await _identity.ResolveOwnerUserIdAsync(User);
        var linkId = long.TryParse(Request.Form["linkId"].FirstOrDefault(), out var id) ? id : (long?)null;
        var newUrl = (Request.Form["newUrl"].FirstOrDefault() ?? "").Trim();
        var newSlug = (Request.Form["newSlug"].FirstOrDefault() ?? "").Trim();

        ShortenedUrl? link;
        if (linkId is not null)
        {
            link = await _db.ShortenedUrls.FirstOrDefaultAsync(l =>
                l.Id == linkId.Value && l.OwnerUserId == ownerUserId);
            if (link is null)
                return await EditorPartialAsync(error: "Link not found.");
        }
        else if (newUrl.Length > 0)
        {
            var created = await CreateLinkAsync(newUrl, newSlug, ownerUserId);
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

        if (await _db.BioPageLinks.AnyAsync(b => b.BioPageId == bioPage.Id && b.ShortenedUrlId == link.Id))
            return await EditorPartialAsync(error: "That link is already on your bio page.");

        var maxOrder = await _db.BioPageLinks
            .Where(b => b.BioPageId == bioPage.Id)
            .Select(b => (int?)b.SortOrder)
            .MaxAsync() ?? -1;

        var title = (Request.Form["title"].FirstOrDefault() ?? "").Trim();
        _db.BioPageLinks.Add(new BioPageLink
        {
            BioPageId = bioPage.Id,
            ShortenedUrlId = link.Id,
            Title = title.Length > 0 ? title : link.ShortCode,
            SortOrder = maxOrder + 1,
            IsVisible = true
        });
        await _db.SaveChangesAsync();

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

        var entry = await _db.BioPageLinks.FirstOrDefaultAsync(b => b.Id == id && b.BioPageId == bioPage.Id);
        if (entry is null)
            return await EditorPartialAsync(error: "Link not found on your bio page.");

        _db.BioPageLinks.Remove(entry);
        await _db.SaveChangesAsync();

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

        var entries = await _db.BioPageLinks
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
        await _db.SaveChangesAsync();

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

        var entry = await _db.BioPageLinks.FirstOrDefaultAsync(b => b.Id == id && b.BioPageId == bioPage.Id);
        if (entry is null)
            return await EditorPartialAsync(error: "Link not found on your bio page.");

        entry.IsVisible = !entry.IsVisible;
        await _db.SaveChangesAsync();

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
        var ownerUserId = await _identity.ResolveOwnerUserIdAsync(User);
        BioPage = ownerUserId is null
            ? null
            : await _db.BioPages
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

        OwnerLinks = ownerUserId is null
            ? []
            : await _db.ShortenedUrls
                .Include(l => l.Domain)
                .Where(l => l.OwnerUserId == ownerUserId)
                .OrderByDescending(l => l.CreatedAtUtc)
                .Take(100)
                .ToListAsync();
    }

    private async Task<BioPage?> FindOwnedPageAsync()
    {
        var ownerUserId = await _identity.ResolveOwnerUserIdAsync(User);
        return ownerUserId is null
            ? null
            : await _db.BioPages.FirstOrDefaultAsync(b => b.OwnerUserId == ownerUserId);
    }

    /// <summary>
    /// Creates a new <see cref="ShortenedUrl"/> on the instance host, mirroring the
    /// Index page's creation rules (valid http(s) URL, slug rules via
    /// <see cref="ShortLinkCodes"/>). Returns an error message on failure.
    /// </summary>
    private async Task<(ShortenedUrl? Link, string? Error)> CreateLinkAsync(string url, string slug, long? ownerUserId)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return (null, "Enter a valid http(s) URL.");

        string code;
        if (slug.Length > 0)
        {
            if (!ShortLinkCodes.IsValidSlug(slug))
                return (null, "Custom code must be 1–64 characters: letters, digits, '-' and '_', starting with a letter or digit.");
            if (await _db.ShortenedUrls.AnyAsync(l => l.DomainId == null && l.ShortCode == slug))
                return (null, $"The custom code '{slug}' is already taken.");
            code = slug;
        }
        else
        {
            code = await ShortLinkCodes.GenerateUniqueCodeAsync(c =>
                _db.ShortenedUrls.AnyAsync(l => l.DomainId == null && l.ShortCode == c));
        }

        var link = new ShortenedUrl
        {
            LongUrl = url,
            ShortCode = code,
            DomainId = null,
            OwnerUserId = ownerUserId,
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.ShortenedUrls.Add(link);
        await _db.SaveChangesAsync();
        return (link, null);
    }

    private IActionResult? EnforceAccess()
    {
        if (_identity.IsAuthEnabled && User.Identity?.IsAuthenticated != true)
            return Request.Headers["HX-Request"].Count > 0
                ? Unauthorized()
                : RedirectToPage("/Index");

        return null;
    }

    private static string NormaliseTheme(string? theme) =>
        BioThemes.IsValid(theme) ? theme! : "default";

    private static string? CleanUrl(string? url)
    {
        url = url?.Trim();
        return string.IsNullOrEmpty(url) ? null : url;
    }
}
