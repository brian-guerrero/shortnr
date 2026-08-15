namespace Shortnr.Web.Features.Infrastructure;

/// <summary>
/// Current sort of an HTMX-sortable table, plus the URL/aria bookkeeping each column header needs.
/// Built once per view from the query string; hands out a <see cref="SortableHeaderViewModel"/> per
/// column so <c>Shared/_SortableHeader</c> stays dumb.
/// </summary>
public class TableSortState
{
    public required string SortParam { get; init; }

    public required string DirParam { get; init; }

    public required string BaseUrl { get; init; }

    /// <summary>The <c>hx-target</c> the sorted table swaps into.</summary>
    public required string Target { get; init; }

    public string CurrentSort { get; init; } = "";

    public string CurrentDir { get; init; } = "";

    /// <summary>Extra selectors to post along with the sort (search box, filters).</summary>
    public string? HxInclude { get; init; }

    /// <summary>Extra query pairs preserved across a sort, e.g. the clicks-per-page limit.</summary>
    public IReadOnlyDictionary<string, string?> ExtraQuery { get; init; }
        = new Dictionary<string, string?>();

    public static TableSortState FromQuery(
        HttpRequest request,
        string sortParam,
        string dirParam,
        string baseUrl,
        string target,
        string? hxInclude = null,
        IReadOnlyDictionary<string, string?>? extraQuery = null) => new()
        {
            SortParam = sortParam,
            DirParam = dirParam,
            BaseUrl = baseUrl,
            Target = target,
            CurrentSort = request.Query[sortParam].FirstOrDefault() ?? "",
            CurrentDir = request.Query[dirParam].FirstOrDefault() ?? "",
            HxInclude = hxInclude,
            ExtraQuery = extraQuery ?? new Dictionary<string, string?>(),
        };

    /// <summary>Builds the header model for one column, resolving active state and the next direction.</summary>
    public SortableHeaderViewModel Header(string column, string label)
    {
        var isActive = CurrentSort == column;
        var isAscending = isActive && CurrentDir == "asc";
        var nextDir = isAscending ? "desc" : "asc";

        var query = new List<string> { $"{SortParam}={column}", $"{DirParam}={nextDir}" };
        query.AddRange(ExtraQuery
            .Where(kvp => !string.IsNullOrEmpty(kvp.Value))
            .Select(kvp => $"{kvp.Key}={kvp.Value}"));

        return new SortableHeaderViewModel
        {
            Label = label,
            IsActive = isActive,
            IsAscending = isAscending,
            AriaSort = !isActive ? "none" : isAscending ? "ascending" : "descending",
            Url = $"{BaseUrl}?{string.Join('&', query)}",
            Target = Target,
            HxInclude = HxInclude,
        };
    }
}

/// <summary>Model for the shared <c>Shared/_SortableHeader</c> partial — one <c>&lt;th&gt;</c>.</summary>
public class SortableHeaderViewModel
{
    public required string Label { get; init; }

    public required bool IsActive { get; init; }

    public required bool IsAscending { get; init; }

    public required string AriaSort { get; init; }

    public required string Url { get; init; }

    public required string Target { get; init; }

    public string? HxInclude { get; init; }
}
