using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;

namespace Shortnr.Web.Pages.Settings;

public partial class DomainsModel(AppDbContext db, UserIdentityService identity, DomainVerifierService verifier)
    : AuthorizedPageModel(identity)
{
    [GeneratedRegex(@"^(?=.{1,253}$)[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?(\.[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)+$")]
    private static partial Regex HostnamePattern();

    public List<Domain> Domains { get; set; } = [];
    public ActiveWorkspaceContext? Workspace { get; set; }

    public async Task<IActionResult> OnGet()
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        Workspace = await identity.ResolveActiveWorkspaceContextAsync(User);
        Domains = await LoadDomainsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPost()
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        Workspace = await identity.ResolveActiveWorkspaceContextAsync(User);
        var workspaceId = Workspace?.WorkspaceId;
        var ownerUserId = await identity.ResolveOwnerUserIdAsync(User);

        var hostname = (Request.Form["hostname"].FirstOrDefault() ?? "").Trim().ToLowerInvariant();
        if (!HostnamePattern().IsMatch(hostname))
            return await ListPartialAsync(error: "Enter a valid domain name, e.g. go.example.com.");

        var exists = await db.Domains.AnyAsync(d => d.Hostname == hostname);
        if (exists)
            return await ListPartialAsync(error: $"The domain '{hostname}' is already registered.");

        db.Domains.Add(new Domain
        {
            Hostname = hostname,
            OwnerUserId = workspaceId is not null ? null : ownerUserId,
            WorkspaceId = workspaceId,
            IsVerified = false,
            IsDefault = false,
            VerificationToken = GenerateVerificationToken(),
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        return await ListPartialAsync(status: $"Domain '{hostname}' added. Point its DNS at this instance, then serve the token below at https://{hostname}/.well-known/shortnr-verify.txt and click Verify.");
    }

    public async Task<IActionResult> OnPostVerify(long id, string method)
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        Workspace = await identity.ResolveActiveWorkspaceContextAsync(User);

        var domain = await FindOwnedDomainAsync(id);
        if (domain is null)
            return await ListPartialAsync(error: "Domain not found.");

        var verified = method == "txt"
            ? await verifier.VerifyByTxtAsync(domain.Hostname, domain.VerificationToken)
            : await verifier.VerifyAsync(domain.Hostname, domain.VerificationToken);

        if (verified)
        {
            domain.IsVerified = true;

            var ownerUserId = await identity.ResolveOwnerUserIdAsync(User);
            var workspaceId = domain.WorkspaceId;

            var hasDefault = await db.Domains
                .Where(d => d.Id != domain.Id && d.IsDefault)
                .ScopeToOwnerStrict(ownerUserId, workspaceId)
                .AnyAsync();

            if (!hasDefault)
                await MakeDefaultAsync(domain, ownerUserId, workspaceId);

            await db.SaveChangesAsync();
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

        Workspace = await identity.ResolveActiveWorkspaceContextAsync(User);

        var domain = await FindOwnedDomainAsync(id);
        if (domain is null)
            return await ListPartialAsync(error: "Domain not found.");
        if (!domain.IsVerified)
            return await ListPartialAsync(error: "Only verified domains can be the default.");

        var ownerUserId = await identity.ResolveOwnerUserIdAsync(User);
        await MakeDefaultAsync(domain, ownerUserId, domain.WorkspaceId);
        await db.SaveChangesAsync();

        return await ListPartialAsync(status: $"Domain '{domain.Hostname}' is now the default. Existing links without a domain now use it.");
    }

    /// <summary>
    /// Marks the given verified domain as the owner's default, clears any other
    /// default, and migrates the owner's links that have no domain yet onto it
    /// (skipping any whose short code already exists on that domain).
    /// </summary>
    private async Task MakeDefaultAsync(Domain domain, long? ownerUserId, long? workspaceId)
    {
        var others = await db.Domains
            .Where(d => d.Id != domain.Id && d.IsDefault)
            .ScopeToOwnerStrict(ownerUserId, workspaceId)
            .ToListAsync();
        foreach (var other in others)
            other.IsDefault = false;

        domain.IsDefault = true;

        var existingCodes = await db.ShortenedUrls
            .Where(l => l.DomainId == domain.Id)
            .Select(l => l.ShortCode)
            .ToHashSetAsync();

        var links = await db.ShortenedUrls
            .Where(l => l.DomainId == null)
            .ScopeToOwnerStrict(ownerUserId, workspaceId)
            .ToListAsync();

        foreach (var link in links)
        {
            if (!existingCodes.Contains(link.ShortCode))
                link.DomainId = domain.Id;
        }
    }

    public async Task<IActionResult> OnPostDelete(long id)
    {
        var gate = EnforceAccess();
        if (gate is not null)
            return gate;

        Workspace = await identity.ResolveActiveWorkspaceContextAsync(User);

        var domain = await FindOwnedDomainAsync(id);
        if (domain is null)
            return await ListPartialAsync(error: "Domain not found.");

        var hostname = domain.Hostname;
        db.Domains.Remove(domain);
        await db.SaveChangesAsync();

        return await ListPartialAsync(status: $"Domain '{hostname}' removed. Existing links now use the default host.");
    }


    private async Task<List<Domain>> LoadDomainsAsync()
    {
        var workspaceId = Workspace?.WorkspaceId;
        var ownerUserId = await identity.ResolveOwnerUserIdAsync(User);
        return await db.Domains
            .ScopeToOwner(ownerUserId, workspaceId)
            .AsNoTracking()
            .OrderBy(d => d.CreatedAtUtc)
            .ToListAsync();
    }

    private async Task<Domain?> FindOwnedDomainAsync(long id)
    {
        var workspaceId = Workspace?.WorkspaceId;
        var ownerUserId = await identity.ResolveOwnerUserIdAsync(User);
        return await db.Domains
            .ScopeToOwnerStrict(ownerUserId, workspaceId)
            .FirstOrDefaultAsync(d => d.Id == id);
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
