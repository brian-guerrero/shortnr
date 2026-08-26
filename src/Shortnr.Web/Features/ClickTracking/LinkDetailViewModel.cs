using Shortnr.Data.Entities;
using Shortnr.Web.Features.Infrastructure;

namespace Shortnr.Web.Features.ClickTracking;

/// <summary>
/// Backs the <c>Shared/_BulkActionResult</c> partial returned by every bulk
/// action (PRD-019). It renders the refreshed search-results table as the
/// swapped content and the confirmation toast out-of-band.
/// </summary>
public class BulkActionResultViewModel
{
    public List<LinkRowViewModel> Links { get; init; } = [];
    public required string Message { get; init; }
    public StatusKind Kind { get; init; } = StatusKind.Info;

    /// <summary>When set (bulk delete only), the toast shows an Undo button.</summary>
    public string? UndoToken { get; init; }
}

/// <summary>
/// Shape of a link row in the dashboard's search-results table (PRD-019). A
/// projection type rather than the raw entity so the row can carry inline stats
/// — last-clicked timestamp, tag stamps — that the table renders without an
/// extra query per row.
/// </summary>
public class LinkRowViewModel
{
    public long Id { get; init; }
    public string ShortCode { get; init; } = "";
    public string LongUrl { get; init; } = "";
    public DateTime CreatedAtUtc { get; init; }
    public long ClickCount { get; init; }
    public DateTime? LastClickedAtUtc { get; init; }
    public string DomainHostname { get; init; } = "";
    public bool IsArchived { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];

    public string DisplayText =>
        DomainHostname.Length > 0 ? $"{DomainHostname}/{ShortCode}" : $"/{ShortCode}";

    /// <summary>
    /// Fallback mapper used by flows that already hold a fully-loaded
    /// <see cref="ShortenedUrl"/> (edit/transfer success) and just need the
    /// row shape; the last-clicked column renders a dash because the source
    /// query didn't compute it.
    /// </summary>
    public static LinkRowViewModel From(ShortenedUrl link) => new()
    {
        Id = link.Id,
        ShortCode = link.ShortCode,
        LongUrl = link.LongUrl,
        CreatedAtUtc = link.CreatedAtUtc,
        ClickCount = link.ClickCount,
        LastClickedAtUtc = null,
        DomainHostname = link.Domain?.Hostname ?? "",
        IsArchived = link.IsArchived,
        Tags = link.Tags?.Select(t => t.Name).ToList() ?? []
    };
}

/// <summary>
/// Backs the <c>Shared/_LinkDetail</c> drill-down modal (PRD-019): click
/// timeline, referrer / device / geo breakdowns, and the UTM summary, all
/// loaded for a single link as one HTMX partial fetch.
/// </summary>
public class LinkDetailViewModel
{
    public long Id { get; init; }
    public string ShortCode { get; init; } = "";
    public string LongUrl { get; init; } = "";
    public string? Title { get; init; }
    public string? Description { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? LastClickedAtUtc { get; init; }
    public long ClickCount { get; init; }
    public string DomainHostname { get; init; } = "";
    public bool IsArchived { get; init; }

    public IReadOnlyList<string> Tags { get; init; } = [];
    public IReadOnlyList<TimelinePoint> Timeline { get; init; } = [];
    public IReadOnlyList<NameCountStat> Referrers { get; init; } = [];
    public IReadOnlyList<NameCountStat> Devices { get; init; } = [];
    public IReadOnlyList<NameCountStat> Geo { get; init; } = [];
    public IReadOnlyList<NameCountStat> Cities { get; init; } = [];

    /// <summary>The UTM parameters this link carries (PRD-005), only those set.</summary>
    public IReadOnlyList<NameCountStat> Utm { get; init; } = [];

    /// <summary>JSON for the timeline chart, resolved at render (token-aware).</summary>
    public string ChartJson { get; init; } = "";

    public string DisplayText =>
        DomainHostname.Length > 0 ? $"{DomainHostname}/{ShortCode}" : $"/{ShortCode}";

    public string DisplayHref { get; init; } = "";
}

public class TimelinePoint
{
    public required string Label { get; init; }
    public required long Count { get; init; }
}

public class NameCountStat
{
    public required string Name { get; init; }
    public required long Count { get; init; }
}