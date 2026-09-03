using System.ComponentModel;
using System.Globalization;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Shortnr.Web.Features.Mcp.McpTools;

/// <summary>
/// Long-running MCP tools that stream progress back to the client via MCP progress
/// notifications (<c>notifications/progress</c>): bulk link import and cross-link
/// analytics aggregation. When the client includes a <c>_meta.progressToken</c> in the
/// request, the injected <see cref="IProgress{ProgressNotificationValue}"/> forwards
/// updates over the originating POST SSE stream; without a token the reports are no-ops.
/// </summary>
[McpServerToolType]
public static class McpStreamingTools
{
    /// <summary>Reports progress; no-ops when the client supplied no progress token.</summary>
    private static void Report(IProgress<ProgressNotificationValue> progress, float done, float? total, string message)
        => progress.Report(new ProgressNotificationValue { Progress = done, Total = total, Message = message });

    [McpServerTool(Name = "import_links", Title = "Bulk-import short links from CSV")]
    public static async Task<string> ImportLinks(
        RequestContext<CallToolRequestParams> context,
        AppDbContext db,
        UserIdentityService identity,
        Channel<AiActivityRecord> activity,
        IProgress<ProgressNotificationValue> progress,
        [Description("CSV content: one link per row, optional header 'url[,slug[,utm_campaign]]'. A URL is required; slug and UTM campaign are optional. The header row is auto-detected and skipped.")] string csv,
        [Description("Optional verified domain hostname for imported links; omit to use your default domain")] string? domain = null,
        CancellationToken ct = default)
    {
        var ownerUserId = await McpToolGuard.ResolveOwnerAsync(context, identity);
        if (ownerUserId is null) return McpToolGuard.OwnerError;
        if (!McpToolGuard.HasScope(context, ApiKeyScopes.McpWrite)) return McpToolGuard.WriteScopeError;

        var rows = ParseCsv(csv);
        if (rows.Count == 0)
            return "Error: csv must contain at least one URL.";
        if (rows.Count > 1000)
            return "Error: csv supports at most 1000 rows in a single import.";

        long? domainId = null;
        if (!string.IsNullOrWhiteSpace(domain))
        {
            var hostname = domain.Trim().ToLowerInvariant();
            if (hostname != "default")
            {
                var resolved = await db.Domains.FirstOrDefaultAsync(
                    d => d.Hostname == hostname && d.OwnerUserId == ownerUserId && d.IsVerified, ct);
                if (resolved is null)
                    return $"Error: '{hostname}' is not a verified domain owned by this account.";
                domainId = resolved.Id;
            }
        }
        else
        {
            domainId = await db.Domains
                .Where(d => d.OwnerUserId == ownerUserId && d.IsVerified && d.IsDefault)
                .Select(d => (long?)d.Id)
                .FirstOrDefaultAsync(ct);
        }

        var existingCodes = new HashSet<string>(await db.ShortenedUrls
            .Where(l => l.DomainId == domainId)
            .Select(l => l.ShortCode)
            .ToListAsync(ct), StringComparer.OrdinalIgnoreCase);

        string? domainHostname = domainId is null ? null : await db.Domains
            .Where(d => d.Id == domainId)
            .Select(d => d.Hostname)
            .FirstOrDefaultAsync(ct);

        var imported = new List<ImportedLink>();
        var failures = new List<ImportFailure>();
        var processed = 0;

        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            processed++;

            var (url, slug, campaign) = row;
            var trimmedUrl = url?.Trim() ?? "";
            if (!Uri.TryCreate(trimmedUrl, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
            {
                failures.Add(new ImportFailure(trimmedUrl, slug, "url must be an absolute http(s) URL."));
                Report(progress, processed, rows.Count, $"Imported {imported.Count}/{rows.Count} links.");
                continue;
            }

            var target = trimmedUrl;
            if (!string.IsNullOrWhiteSpace(campaign))
                target = UtmBuilder.AppendUtm(trimmedUrl, new UtmParameters(null, null, campaign.Trim(), null, null));

            var code = (slug?.Trim() ?? "");
            if (code.Length > 0)
            {
                if (!ShortLinkCodes.IsValidSlug(code) || existingCodes.Contains(code))
                {
                    failures.Add(new ImportFailure(trimmedUrl, code, code.Length == 0 || ShortLinkCodes.IsValidSlug(code) ? "short code already in use on this domain" : "invalid slug"));
                    Report(progress, processed, rows.Count, $"Imported {imported.Count}/{rows.Count} links.");
                    continue;
                }
            }
            else
            {
                code = await ShortLinkCodes.GenerateUniqueCodeAsync(candidate => Task.FromResult(existingCodes.Contains(candidate)));
            }
            existingCodes.Add(code);

            db.ShortenedUrls.Add(new ShortenedUrl
            {
                LongUrl = target,
                ShortCode = code,
                DomainId = domainId,
                OwnerUserId = ownerUserId.Value,
                CreatedAtUtc = DateTime.UtcNow
            });
            imported.Add(new ImportedLink(code, domainHostname, target, campaign));

            Report(progress, processed, rows.Count, $"Imported {imported.Count}/{rows.Count} links.");
        }

        if (imported.Count > 0)
            await db.SaveChangesAsync(ct);

        if (imported.Count > 0)
        {
            McpToolGuard.LogActivity(activity, ownerUserId.Value, McpToolGuard.ResolveApiKeyId(context),
                "import_links", nameof(ShortenedUrl), null,
                $"Bulk-imported {imported.Count} links ({failures.Count} failures)");
        }

        return McpToolGuard.Json(new ImportResult(imported.Count, failures.Count, imported, failures));
    }

    [McpServerTool(Name = "aggregate_analytics", Title = "Aggregate click analytics across links", ReadOnly = true)]
    public static async Task<string> AggregateAnalytics(
        RequestContext<CallToolRequestParams> context,
        AppDbContext db,
        UserIdentityService identity,
        IProgress<ProgressNotificationValue> progress,
        [Description("Start date (yyyy-MM-dd, inclusive); omit for all time")] string? from = null,
        [Description("End date (yyyy-MM-dd, inclusive); omit for all time")] string? to = null,
        [Description("Optional workspace slug to scope the aggregation to")] string? workspace = null,
        CancellationToken ct = default)
    {
        var ownerUserId = await McpToolGuard.ResolveOwnerAsync(context, identity);
        if (ownerUserId is null) return McpToolGuard.OwnerError;
        if (!McpToolGuard.HasScope(context, ApiKeyScopes.McpRead)) return McpToolGuard.ReadScopeError;

        var fromValue = ParseDate(from);
        if (fromValue.Error is not null)
            return fromValue.Error;
        var toValue = ParseDate(to);
        if (toValue.Error is not null)
            return toValue.Error;

        Report(progress, 0, null, $"Aggregating clicks{(fromValue.Value is not null || toValue.Value is not null ? $" from {fromValue.Value?.ToString("yyyy-MM-dd")} to {toValue.Value?.ToString("yyyy-MM-dd")}" : " (all time)")}.");

        var links = await McpToolGuard.AccessibleLinksQuery(db, ownerUserId.Value)
            .Include(l => l.Domain)
            .Where(l => string.IsNullOrWhiteSpace(workspace) || (l.Workspace != null && l.Workspace.Slug == workspace.Trim()))
            .Select(l => new { l.Id, l.ShortCode, Domain = l.Domain != null ? l.Domain.Hostname : (string?)null })
            .ToListAsync(ct);

        if (links.Count == 0)
            return McpToolGuard.Json(new AggregateResult(0, 0, [], [], [], [], [], []));

        var totals = new AggregateTotals();
        var perLink = new List<LinkAggregate>();
        var timeline = new Dictionary<DateTime, long>();
        var referrers = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var devices = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var browsers = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var countries = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        var linkIds = links.Select(l => l.Id).ToList();

        Report(progress, 0, links.Count, "Aggregating click events across all links.");

        var baseQuery = db.ClickEvents.Where(e => linkIds.Contains(e.ShortenedUrlId));
        if (fromValue.Value is not null)
            baseQuery = baseQuery.Where(e => e.ClickedAtUtc >= fromValue.Value);
        if (toValue.Value is not null)
            baseQuery = baseQuery.Where(e => e.ClickedAtUtc < toValue.Value.Value.AddDays(1));

        var perLinkCounts = await baseQuery
            .GroupBy(e => e.ShortenedUrlId)
            .Select(g => new { LinkId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var countLookup = perLinkCounts.ToDictionary(x => x.LinkId, x => x.Count);
        var processed = 0;
        foreach (var link in links)
        {
            processed++;
            Report(progress, processed, links.Count, $"Aggregating clicks for {link.ShortCode} ({processed}/{links.Count} links).");

            if (!countLookup.TryGetValue(link.Id, out var count) || count == 0)
                continue;

            totals.TotalClicks += count;
            totals.LinksWithClicks++;
            perLink.Add(new LinkAggregate(link.ShortCode, link.Domain, count));
        }

        var dayRows = await baseQuery
            .GroupBy(e => e.ClickedAtUtc.Date)
            .Select(g => new { Date = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        foreach (var row in dayRows)
            timeline[row.Date] = row.Count;

        foreach (var (key, target) in new[]
        {
            (baseQuery.Where(e => e.Referer != "").GroupBy(e => e.Referer), referrers),
            (baseQuery.Where(e => e.DeviceFamily != null && e.DeviceFamily != "").GroupBy(e => e.DeviceFamily!), devices),
            (baseQuery.Where(e => e.Browser != null && e.Browser != "").GroupBy(e => e.Browser!), browsers),
            (baseQuery.Where(e => e.CountryName != null).GroupBy(e => e.CountryName!), countries)
        })
        {
            var rows = await key
                .Select(g => new { Name = g.Key, Count = g.Count() })
                .ToListAsync(ct);
            foreach (var row in rows)
                target[row.Name] = row.Count;
        }

        return McpToolGuard.Json(new AggregateResult(
            totals.TotalClicks, totals.LinksWithClicks,
            perLink.OrderByDescending(p => p.Clicks).Take(50).ToList(),
            timeline.OrderBy(x => x.Key).Select(x => new DateCount(x.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), x.Value)).ToList(),
            Top(referrers), Top(devices), Top(browsers), Top(countries)));
    }

    /// <summary>Parses CSV text into (url, slug, campaign) rows, auto-detecting and
    /// skipping a header row, dropping blank lines and quoting-aware commas.</summary>
    private static List<(string? Url, string? Slug, string? Campaign)> ParseCsv(string csv)
    {
        var rows = new List<(string? Url, string? Slug, string? Campaign)>();
        foreach (var rawLine in csv.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            var fields = SplitCsvLine(line);
            if (fields.Count == 0)
                continue;

            if (rows.Count == 0 && IsHeader(fields))
                continue;

            var url = fields[0];
            var slug = fields.Count > 1 ? fields[1] : null;
            var campaign = fields.Count > 2 ? fields[2] : null;
            rows.Add((url, slug, campaign));
        }
        return rows;
    }

    /// <summary>Splits a CSV line honoring double-quoted fields.</summary>
    private static List<string> SplitCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        foreach (var ch in line)
        {
            if (ch == '"')
            {
                if (inQuotes && current.Length > 0 && current[^1] == '"')
                {
                    current[^1] = '"';
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (ch == ',' && !inQuotes)
            {
                fields.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }
        fields.Add(current.ToString().Trim());
        return fields;
    }

    private static bool IsHeader(List<string> fields) =>
        fields.Count > 0 && (string.Equals(fields[0], "url", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fields[0], "long_url", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fields[0], "destination", StringComparison.OrdinalIgnoreCase));

    private static List<NameCount> Top(Dictionary<string, long> source) =>
        source.OrderByDescending(x => x.Value).Take(10).Select(x => new NameCount(x.Key, x.Value)).ToList();

    private static (DateTime? Value, string? Error) ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return (null, null);
        if (DateTime.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var date))
            return (date, null);
        return (null, $"Error: invalid date '{value}'. Expected yyyy-MM-dd.");
    }

    private sealed record ImportedLink(string Code, string? Domain, string LongUrl, string? UtmCampaign);
    private sealed record ImportFailure(string Url, string? Slug, string Reason);
    private sealed record ImportResult(int Imported, int Failed, IReadOnlyList<ImportedLink> Links, IReadOnlyList<ImportFailure> Errors);

    private sealed class AggregateTotals { public long TotalClicks; public long LinksWithClicks; }
    private sealed record LinkAggregate(string Code, string? Domain, long Clicks);
    private sealed record DateCount(string Date, long Count);
    private sealed record NameCount(string Name, long Count);
    private sealed record AggregateResult(
        long TotalClicks, long LinksWithClicks,
        IReadOnlyList<LinkAggregate> TopLinks,
        IReadOnlyList<DateCount> Timeline,
        IReadOnlyList<NameCount> Referrers,
        IReadOnlyList<NameCount> Devices,
        IReadOnlyList<NameCount> Browsers,
        IReadOnlyList<NameCount> Countries);
}
