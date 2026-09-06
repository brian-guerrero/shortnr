using Shortnr.Data.Entities;

namespace Shortnr.Web.Features.Workspaces;

/// <summary>
/// Restricts a query to what the current caller may see: the active workspace
/// when one is selected, otherwise the caller's own rows.
///
/// The variants exist because the call sites genuinely differ — a single
/// "ScopeToOwner" would silently change behaviour somewhere. Pick deliberately:
/// <list type="bullet">
///   <item><see cref="ScopeToOwner(IQueryable{ShortenedUrl}, long?, long?)"/> leaves the
///   query unscoped when neither id is set (auth disabled).</item>
///   <item><see cref="ScopeToOwnerStrict{T}"/> always filters, even on a null owner id.</item>
///   <item><see cref="ScopeToOwnerOrBareHost"/> falls back to domainless links.</item>
///   <item><see cref="ScopeToOwnerIncludingWorkspace"/> is an inclusive OR.</item>
/// </list>
///
/// These compose <c>Where</c> eagerly and return <see cref="IQueryable{T}"/>, so
/// EF never sees the call itself and the emitted SQL is unchanged. For that
/// reason they must never be invoked inside a <c>Select</c>/<c>Where</c> lambda.
/// </summary>
public static class ScopingExtensions
{
    public static IQueryable<ShortenedUrl> ScopeToOwner(this IQueryable<ShortenedUrl> query, long? ownerUserId, long? workspaceId)
    {
        if (workspaceId is not null)
            return query.Where(l => l.WorkspaceId == workspaceId);
        if (ownerUserId is not null)
            return query.Where(l => l.OwnerUserId == ownerUserId);
        return query;
    }

    public static IQueryable<ClickEvent> ScopeToOwner(this IQueryable<ClickEvent> query, long? ownerUserId, long? workspaceId)
    {
        if (workspaceId is not null)
            return query.Where(e => e.ShortenedUrl.WorkspaceId == workspaceId);
        if (ownerUserId is not null)
            return query.Where(e => e.ShortenedUrl.OwnerUserId == ownerUserId);
        return query;
    }

    public static IQueryable<TagSuggestion> ScopeToOwner(this IQueryable<TagSuggestion> query, long? ownerUserId, long? workspaceId)
    {
        if (workspaceId is not null)
            return query.Where(s => s.ShortenedUrl.WorkspaceId == workspaceId);
        if (ownerUserId is not null)
            return query.Where(s => s.ShortenedUrl.OwnerUserId == ownerUserId);
        return query;
    }

    public static IQueryable<Domain> ScopeToOwner(this IQueryable<Domain> query, long? ownerUserId, long? workspaceId)
    {
        if (workspaceId is not null)
            return query.Where(d => d.WorkspaceId == workspaceId);
        if (ownerUserId is not null)
            return query.Where(d => d.OwnerUserId == ownerUserId);
        return query;
    }

    /// <summary>
    /// Workspace when set, otherwise the owner — with no unscoped escape hatch, so a
    /// null <paramref name="ownerUserId"/> matches only rows that are themselves unowned.
    /// </summary>
    public static IQueryable<Domain> ScopeToOwnerStrict(this IQueryable<Domain> query, long? ownerUserId, long? workspaceId) =>
        workspaceId is not null
            ? query.Where(d => d.WorkspaceId == workspaceId)
            : query.Where(d => d.OwnerUserId == ownerUserId);

    /// <inheritdoc cref="ScopeToOwnerStrict(IQueryable{Domain}, long?, long?)"/>
    public static IQueryable<ShortenedUrl> ScopeToOwnerStrict(this IQueryable<ShortenedUrl> query, long? ownerUserId, long? workspaceId) =>
        workspaceId is not null
            ? query.Where(l => l.WorkspaceId == workspaceId)
            : query.Where(l => l.OwnerUserId == ownerUserId);

    /// <summary>
    /// Home page variant: with no owner and no workspace (auth disabled), show the
    /// links on the bare instance host rather than every link in the database.
    /// </summary>
    public static IQueryable<ShortenedUrl> ScopeToOwnerOrBareHost(this IQueryable<ShortenedUrl> query, long? ownerUserId, long? workspaceId)
    {
        if (workspaceId is not null)
            return query.Where(l => l.WorkspaceId == workspaceId);
        if (ownerUserId is not null)
            return query.Where(l => l.OwnerUserId == ownerUserId);
        return query.Where(l => l.DomainId == null);
    }

    /// <summary>
    /// Bio editor variant: an inclusive OR, so the link picker offers the owner's
    /// personal links alongside the active workspace's links.
    /// </summary>
    public static IQueryable<ShortenedUrl> ScopeToOwnerIncludingWorkspace(this IQueryable<ShortenedUrl> query, long? ownerUserId, long? workspaceId) =>
        workspaceId is not null
            ? query.Where(l => l.OwnerUserId == ownerUserId || l.WorkspaceId == workspaceId)
            : query.Where(l => l.OwnerUserId == ownerUserId);

    /// <summary>
    /// Filters to a domain by hostname; the sentinel <c>"default"</c> selects links
    /// on the bare instance host. Hostnames are matched case-insensitively.
    /// </summary>
    public static IQueryable<ShortenedUrl> FilterByDomain(this IQueryable<ShortenedUrl> query, string? domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
            return query;

        var hostname = domain.Trim().ToLowerInvariant();
        return hostname == "default"
            ? query.Where(l => l.DomainId == null)
            : query.Where(l => l.Domain != null && l.Domain.Hostname == hostname);
    }
}
