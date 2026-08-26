using System.Collections.Concurrent;
using System.Security.Cryptography;
using Shortnr.Data.Entities;

namespace Shortnr.Web.Features.ShortLinks;

/// <summary>
/// Short-lived in-memory store of links deleted by the dashboard's bulk-delete
/// action, keyed by an opaque token the "Undo" toast returns. Deleted links are
/// resumable: tags and metadata are snapshotted alongside the link so an undo
/// restores the row exactly as it was.
/// <para>
/// Deliberately process-local and bounded (20 snapshots, 5-minute TTL). It is a
/// razor-thin undo affordance for a destructive UI action, not a recycle bin —
/// after a restart the snapshot is gone and the delete stays deleted.
/// </para>
/// </summary>
public class BulkLinkUndoService
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    private const int MaxSnapshots = 20;

    private readonly ConcurrentDictionary<string, Snapshot> _snapshots = new();

    /// <summary>Stashes a copy of the given links and returns the token to undo with.</summary>
    public string Capture(IEnumerable<ShortenedUrl> links)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(18))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        _snapshots[token] = new Snapshot(DateTime.UtcNow + Ttl, links.Select(LinkSnapshot.From).ToList());
        PruneExpired();

        // Bounded: once full, the oldest snapshot loses its undo.
        while (_snapshots.Count > MaxSnapshots)
        {
            var oldest = _snapshots.OrderBy(kvp => kvp.Value.ExpiresAt).First();
            _snapshots.TryRemove(oldest.Key, out _);
        }

        return token;
    }

    /// <summary>
    /// Returns the snapshotted links for <paramref name="token"/>, or
    /// <c>null</c> when the token is unknown or its snapshot has expired.
    /// </summary>
    public IReadOnlyList<ShortenedUrl>? Retrieve(string token)
    {
        if (!_snapshots.TryGetValue(token, out var snapshot))
            return null;
        if (snapshot.ExpiresAt < DateTime.UtcNow)
        {
            _snapshots.TryRemove(token, out _);
            return null;
        }
        _snapshots.TryRemove(token, out _); // Undo is one-shot.
        return snapshot.Links.Select(l => l.ToEntity()).ToList();
    }

    private void PruneExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var (token, snapshot) in _snapshots)
        {
            if (snapshot.ExpiresAt < now)
                _snapshots.TryRemove(token, out _);
        }
    }

    private sealed record Snapshot(DateTime ExpiresAt, List<LinkSnapshot> Links);

    private sealed class LinkSnapshot
    {
        public long Id { get; init; }
        public string LongUrl { get; init; } = "";
        public string ShortCode { get; init; } = "";
        public DateTime CreatedAtUtc { get; init; }
        public long ClickCount { get; init; }
        public long? OwnerUserId { get; init; }
        public long? DomainId { get; init; }
        public long? WorkspaceId { get; init; }
        public string? Title { get; init; }
        public string? Description { get; init; }
        public DateTime? ArchivedAtUtc { get; init; }
        public DateTime? UpdatedAtUtc { get; init; }
        public List<ShortenedUrlTag> Tags { get; init; } = [];
        public ShortenedUrlMetadata? Metadata { get; init; }

        public static LinkSnapshot From(ShortenedUrl link) => new()
        {
            Id = link.Id,
            LongUrl = link.LongUrl,
            ShortCode = link.ShortCode,
            CreatedAtUtc = link.CreatedAtUtc,
            ClickCount = link.ClickCount,
            OwnerUserId = link.OwnerUserId,
            DomainId = link.DomainId,
            WorkspaceId = link.WorkspaceId,
            Title = link.Title,
            Description = link.Description,
            ArchivedAtUtc = link.ArchivedAtUtc,
            UpdatedAtUtc = link.UpdatedAtUtc,
            Tags = (link.Tags ?? []).Select(t => new ShortenedUrlTag
            {
                ShortenedUrlId = link.Id,
                Name = t.Name,
                CreatedAtUtc = t.CreatedAtUtc
            }).ToList(),
            Metadata = link.Metadata is null ? null : new ShortenedUrlMetadata
            {
                ShortenedUrlId = link.Id,
                UtmSource = link.Metadata.UtmSource,
                UtmMedium = link.Metadata.UtmMedium,
                UtmCampaign = link.Metadata.UtmCampaign,
                UtmTerm = link.Metadata.UtmTerm,
                UtmContent = link.Metadata.UtmContent,
                PixelSnippetId = link.Metadata.PixelSnippetId,
                PixelId = link.Metadata.PixelId,
                IosDeepLink = link.Metadata.IosDeepLink,
                AndroidDeepLink = link.Metadata.AndroidDeepLink
            }
        };

        public ShortenedUrl ToEntity()
        {
            var link = new ShortenedUrl
            {
                Id = Id,
                LongUrl = LongUrl,
                ShortCode = ShortCode,
                CreatedAtUtc = CreatedAtUtc,
                ClickCount = ClickCount,
                OwnerUserId = OwnerUserId,
                DomainId = DomainId,
                WorkspaceId = WorkspaceId,
                Title = Title,
                Description = Description,
                ArchivedAtUtc = ArchivedAtUtc,
                UpdatedAtUtc = UpdatedAtUtc
            };
            link.Tags = Tags;
            if (Metadata is not null)
            {
                link.Metadata = Metadata;
                link.Metadata.ShortenedUrlId = Id;
                link.Metadata.PixelSnippet = null;
            }
            return link;
        }
    }
}