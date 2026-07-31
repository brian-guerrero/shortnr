using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Web.Services;

namespace Shortnr.Web.Pages.Settings;

public class DomainsModel : PageModel
{
    private static readonly Regex HostnamePattern = new(
        @"^(?=.{1,253}$)[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?(\.[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)+$",
        RegexOptions.Compiled);

    private readonly AppDbContext _db;
    private readonly UserIdentityService _identity;
    private readonly DomainVerifierService _verifier;

    public List<Domain> Domains { get; set; } = [];
    public string? StatusMessage { get; set; }
    public string? ErrorMessage { get; set; }
    public bool IsHtmxRequest { get; set; }

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
        Domains = await LoadDomainsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPost()
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        var hostname = (Request.Form["hostname"].FirstOrDefault() ?? "").Trim().ToLowerInvariant();
        if (!HostnamePattern.IsMatch(hostname))
            return await ListPartialAsync(error: "Enter a valid domain name, e.g. go.example.com.");

        var exists = await _db.Domains.AnyAsync(d => d.Hostname == hostname);
        if (exists)
            return await ListPartialAsync(error: $"The domain '{hostname}' is already registered.");

        _db.Domains.Add(new Domain
        {
            Hostname = hostname,
            OwnerUserId = await _identity.ResolveOwnerUserIdAsync(User),
            IsVerified = false,
            IsDefault = false,
            VerificationToken = GenerateVerificationToken(),
            CreatedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        return await ListPartialAsync(status: $"Domain '{hostname}' added. Point its DNS at this instance, then serve the token below at https://{hostname}/.well-known/shortnr-verify.txt and click Verify.");
    }

    public async Task<IActionResult> OnPostVerify(long id)
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        var domain = await FindOwnedDomainAsync(id);
        if (domain is null)
            return await ListPartialAsync(error: "Domain not found.");

        var verified = await _verifier.VerifyAsync(domain.Hostname, domain.VerificationToken);
        if (verified)
        {
            domain.IsVerified = true;

            // First verified domain becomes the default for new links automatically.
            var ownerUserId = await _identity.ResolveOwnerUserIdAsync(User);
            var hasDefault = await _db.Domains.AnyAsync(d =>
                d.OwnerUserId == ownerUserId && d.Id != domain.Id && d.IsDefault);
            if (!hasDefault)
                domain.IsDefault = true;

            await _db.SaveChangesAsync();
            var message = domain.IsDefault
                ? $"Domain '{domain.Hostname}' verified and set as the default domain for new links."
                : $"Domain '{domain.Hostname}' verified.";
            return await ListPartialAsync(status: message);
        }

        return await ListPartialAsync(error: $"Verification failed for '{domain.Hostname}'. Confirm the domain points at this instance and that /.well-known/shortnr-verify.txt serves the token shown below, then retry.");
    }

    public async Task<IActionResult> OnPostSetDefault(long id)
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        var domain = await FindOwnedDomainAsync(id);
        if (domain is null)
            return await ListPartialAsync(error: "Domain not found.");
        if (!domain.IsVerified)
            return await ListPartialAsync(error: "Only verified domains can be the default.");

        var ownerUserId = await _identity.ResolveOwnerUserIdAsync(User);
        var others = await _db.Domains
            .Where(d => d.OwnerUserId == ownerUserId && d.Id != id && d.IsDefault)
            .ToListAsync();
        foreach (var other in others)
            other.IsDefault = false;

        domain.IsDefault = true;
        await _db.SaveChangesAsync();

        return await ListPartialAsync(status: $"Domain '{domain.Hostname}' is now the default domain for new links.");
    }

    public async Task<IActionResult> OnPostDelete(long id)
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

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
        var ownerUserId = await _identity.ResolveOwnerUserIdAsync(User);
        var query = _db.Domains.AsQueryable();
        if (ownerUserId is not null)
            query = query.Where(d => d.OwnerUserId == ownerUserId);

        return await query.OrderBy(d => d.CreatedAtUtc).ToListAsync();
    }

    private async Task<Domain?> FindOwnedDomainAsync(long id)
    {
        var ownerUserId = await _identity.ResolveOwnerUserIdAsync(User);
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
