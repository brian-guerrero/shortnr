using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;

namespace Shortnr.Web.Pages.Settings;

public partial class DomainsModel : PageModel, IStatusMessages
{
    [GeneratedRegex(@"^(?=.{1,253}$)[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?(\.[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)+$")]
    private static partial Regex HostnamePattern();

    private readonly AppDbContext _db;
    private readonly UserIdentityService _identity;
    private readonly DomainVerifierService _verifier;

    public List<Domain> Domains { get; set; } = [];
    public string? StatusMessage { get; set; }
    public string? ErrorMessage { get; set; }
    public bool IsHtmxRequest { get; set; }
    public ActiveWorkspaceContext? Workspace { get; set; }

    public DomainsModel(AppDbContext db, UserIdentityService identity, DomainVerifierService verifier)
    {
        _db = db;
        _identity = identity;
        _verifier = verifier;
    }

    public async Task<IActionResult> OnGet()
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        IsHtmxRequest = Request.Headers["HX-Request"].Count > 0;
        Workspace = await _identity.ResolveActiveWorkspaceContextAsync(User);
        Domains = await LoadDomainsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPost()
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        Workspace = await _identity.ResolveActiveWorkspaceContextAsync(User);
        var workspaceId = Workspace?.WorkspaceId;
        var ownerUserId = await _identity.ResolveOwnerUserIdAsync(User);

        var hostname = (Request.Form["hostname"].FirstOrDefault() ?? "").Trim().ToLowerInvariant();
        if (!HostnamePattern().IsMatch(hostname))
            return await ListPartialAsync(error: "Enter a valid domain name, e.g. go.example.com.");

        var exists = await _db.Domains.AnyAsync(d => d.Hostname == hostname);
        if (exists)
            return await ListPartialAsync(error: $"The domain '{hostname}' is already registered.");

        _db.Domains.Add(new Domain
        {
            Hostname = hostname,
            OwnerUserId = workspaceId is not null ? null : ownerUserId,
            WorkspaceId = workspaceId,
            IsVerified = false,
            IsDefault = false,
            VerificationToken = GenerateVerificationToken(),
            CreatedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        return await ListPartialAsync(status: $"Domain '{hostname}' added. Point its DNS at this instance, then serve the token below at https://{hostname}/.well-known/shortnr-verify.txt and click Verify.");
    }

    public async Task<IActionResult> OnPostVerify(long id, string method)
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        Workspace = await _identity.ResolveActiveWorkspaceContextAsync(User);

        var domain = await FindOwnedDomainAsync(id);
        if (domain is null)
            return await ListPartialAsync(error: "Domain not found.");

        var verified = method == "txt"
            ? await _verifier.VerifyByTxtAsync(domain.Hostname, domain.VerificationToken)
            : await _verifier.VerifyAsync(domain.Hostname, domain.VerificationToken);

        if (verified)
        {
            domain.IsVerified = true;

            var ownerUserId = await _identity.ResolveOwnerUserIdAsync(User);
            var workspaceId = domain.WorkspaceId;

            var hasDefaultQuery = _db.Domains.Where(d => d.Id != domain.Id && d.IsDefault);
            if (workspaceId is not null)
                hasDefaultQuery = hasDefaultQuery.Where(d => d.WorkspaceId == workspaceId);
            else
                hasDefaultQuery = hasDefaultQuery.Where(d => d.OwnerUserId == ownerUserId);
            var hasDefault = await hasDefaultQuery.AnyAsync();

            if (!hasDefault)
                await MakeDefaultAsync(domain, ownerUserId, workspaceId);

            await _db.SaveChangesAsync();
            var message = domain.IsDefault
                ? $"Domain '{domain.Hostname}' verified and set as the default domain. Existing links without a domain now use it."
                : $"Domain '{domain.Hostname}' verified.";
            return await ListPartialAsync(status: message);
        }

        return await ListPartialAsync(error: $"Verification failed for '{domain.Hostname}'. Confirm the domain points at this instance and that the verification token is served at https://{domain.Hostname}/.well-known/shortnr-verify.txt (file method) or as a TXT record at _shortnr-verify.{domain.Hostname} (DNS method), then retry.");
    }

    public async Task<IActionResult> OnPostSetDefault(long id)
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        Workspace = await _identity.ResolveActiveWorkspaceContextAsync(User);

        var domain = await FindOwnedDomainAsync(id);
        if (domain is null)
            return await ListPartialAsync(error: "Domain not found.");
        if (!domain.IsVerified)
            return await ListPartialAsync(error: "Only verified domains can be the default.");

        var ownerUserId = await _identity.ResolveOwnerUserIdAsync(User);
        await MakeDefaultAsync(domain, ownerUserId, domain.WorkspaceId);
        await _db.SaveChangesAsync();

        return await ListPartialAsync(status: $"Domain '{domain.Hostname}' is now the default. Existing links without a domain now use it.");
    }

    /// <summary>
    /// Marks the given verified domain as the owner's default, clears any other
    /// default, and migrates the owner's links that have no domain yet onto it
    /// (skipping any whose short code already exists on that domain).
    /// </summary>
    private async Task MakeDefaultAsync(Domain domain, long? ownerUserId, long? workspaceId)
    {
        var othersQuery = _db.Domains.Where(d => d.Id != domain.Id && d.IsDefault);
        if (workspaceId is not null)
            othersQuery = othersQuery.Where(d => d.WorkspaceId == workspaceId);
        else
            othersQuery = othersQuery.Where(d => d.OwnerUserId == ownerUserId);
        await othersQuery.ExecuteUpdateAsync(s => s.SetProperty(d => d.IsDefault, false));

        domain.IsDefault = true;

        var existingCodes = await _db.ShortenedUrls
            .Where(l => l.DomainId == domain.Id)
            .Select(l => l.ShortCode)
            .ToHashSetAsync();

        var linksToMigrate = _db.ShortenedUrls.Where(l => l.DomainId == null);
        if (workspaceId is not null)
            linksToMigrate = linksToMigrate.Where(l => l.WorkspaceId == workspaceId);
        else
            linksToMigrate = linksToMigrate.Where(l => l.OwnerUserId == ownerUserId);

        if (existingCodes.Count > 0)
            linksToMigrate = linksToMigrate.Where(l => !existingCodes.Contains(l.ShortCode));

        await linksToMigrate
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.DomainId, domain.Id));
    }

    public async Task<IActionResult> OnPostDelete(long id)
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        Workspace = await _identity.ResolveActiveWorkspaceContextAsync(User);

        var domain = await FindOwnedDomainAsync(id);
        if (domain is null)
            return await ListPartialAsync(error: "Domain not found.");

        var hostname = domain.Hostname;
        _db.Domains.Remove(domain);
        await _db.SaveChangesAsync();

        return await ListPartialAsync(status: $"Domain '{hostname}' removed. Existing links now use the default host.");
    }

    private IActionResult? EnforceAccess()
    {
        if (_identity.IsAuthEnabled && User.Identity?.IsAuthenticated != true)
            return Request.Headers["HX-Request"].Count > 0
                ? Unauthorized()
                : RedirectToPage("/Index");

        return null;
    }

    private async Task<List<Domain>> LoadDomainsAsync()
    {
        var workspaceId = Workspace?.WorkspaceId;
        var ownerUserId = await _identity.ResolveOwnerUserIdAsync(User);
        var query = _db.Domains.AsQueryable();
        if (workspaceId is not null)
            query = query.Where(d => d.WorkspaceId == workspaceId);
        else if (ownerUserId is not null)
            query = query.Where(d => d.OwnerUserId == ownerUserId);

        return await query.AsNoTracking().OrderBy(d => d.CreatedAtUtc).ToListAsync();
    }

    private async Task<Domain?> FindOwnedDomainAsync(long id)
    {
        var workspaceId = Workspace?.WorkspaceId;
        var ownerUserId = await _identity.ResolveOwnerUserIdAsync(User);
        if (workspaceId is not null)
            return await _db.Domains.FirstOrDefaultAsync(d => d.Id == id && d.WorkspaceId == workspaceId);
        return await _db.Domains.FirstOrDefaultAsync(d => d.Id == id && d.OwnerUserId == ownerUserId);
    }

    private async Task<IActionResult> ListPartialAsync(string? status = null, string? error = null)
    {
        StatusMessage = status;
        ErrorMessage = error;
        Domains = await LoadDomainsAsync();
        return Partial("Shared/_DomainsList", this);
    }

    private static string GenerateVerificationToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
}
