using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;

namespace Shortnr.Web.Features.ClickTracking;

public sealed class ClickBatchProcessor : BackgroundService
{
    private readonly Channel<ClickRecord> _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ClickBatchProcessor> _logger;
    private readonly GeoIpService _geoIp;
    private readonly Channel<object> _sseChannel;
    private readonly WebhookEventDispatcher _webhookDispatcher;

    public ClickBatchProcessor(Channel<ClickRecord> channel, IServiceScopeFactory scopeFactory,
        ILogger<ClickBatchProcessor> logger, GeoIpService geoIp, Channel<object> sseChannel,
        WebhookEventDispatcher webhookDispatcher)
    {
        _channel = channel;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _geoIp = geoIp;
        _sseChannel = sseChannel;
        _webhookDispatcher = webhookDispatcher;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ClickBatchProcessor starting");
        var buffer = new List<ClickRecord>(100);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _channel.Reader.WaitToReadAsync(stoppingToken);
                if (stoppingToken.IsCancellationRequested) break;

                while (buffer.Count < 100 && _channel.Reader.TryRead(out var record))
                {
                    buffer.Add(record);
                }

                if (buffer.Count == 0) continue;

                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var ids = buffer.Select(r => r.ShortenedUrlId).Distinct().ToList();
                var existingIds = await db.ShortenedUrls
                    .Where(u => ids.Contains(u.Id))
                    .Select(u => u.Id)
                    .ToHashSetAsync(stoppingToken);

                var clickCountDelta = new Dictionary<long, int>();
                var now = DateTime.UtcNow;

                var events = new List<ClickEvent>(buffer.Count);
                foreach (var record in buffer)
                {
                    if (!existingIds.Contains(record.ShortenedUrlId)) continue;

                    clickCountDelta[record.ShortenedUrlId] = clickCountDelta.GetValueOrDefault(record.ShortenedUrlId) + 1;

                    var uaInfo = SafeUserAgentParser.Parse(record.UserAgent);

                    var clickEvent = new ClickEvent
                    {
                        ShortenedUrlId = record.ShortenedUrlId,
                        IpAddress = record.IpAddress,
                        UserAgent = record.UserAgent,
                        Referer = record.Referer,
                        ClickedAtUtc = now,
                        DeviceFamily = uaInfo?.DeviceType ?? uaInfo?.DeviceModel,
                        OperatingSystem = uaInfo?.OsName,
                        OSVersion = uaInfo?.OsVersion,
                        Browser = uaInfo?.BrowserName,
                        BrowserVersion = uaInfo?.BrowserMajor is not null
                            ? uaInfo.BrowserMajor + (uaInfo.BrowserVersion is not null ? "." + uaInfo.BrowserVersion : "")
                            : uaInfo?.BrowserVersion
                    };

                    EnrichGeo(record.IpAddress, clickEvent);
                    events.Add(clickEvent);
                }

                if (events.Count == 0) { buffer.Clear(); continue; }

                using var tx = await db.Database.BeginTransactionAsync(stoppingToken);

                var insertSql = BuildMultiRowInsert(events);
                await db.Database.ExecuteSqlInterpolatedAsync(insertSql, stoppingToken);

                foreach (var (urlId, delta) in clickCountDelta)
                {
                    await db.ShortenedUrls
                        .Where(u => u.Id == urlId)
                        .ExecuteUpdateAsync(s => s.SetProperty(u => u.ClickCount, u => u.ClickCount + delta), stoppingToken);
                }

                await tx.CommitAsync(stoppingToken);

                _logger.LogInformation("Processed {Count} click events in one batch — notifying SSE clients", events.Count);
                _sseChannel.Writer.TryWrite(new object());

                await DispatchWebhookEventsAsync(db, clickCountDelta, buffer, now, stoppingToken);

                buffer.Clear();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing click batch");
            }
        }

        _logger.LogInformation("ClickBatchProcessor stopping");
    }

    private static FormattableString BuildMultiRowInsert(List<ClickEvent> events)
    {
        var format = new StringBuilder();
        format.Append("INSERT INTO \"ClickEvents\" (");
        format.Append("\"ShortenedUrlId\", \"IpAddress\", \"UserAgent\", \"Referer\", \"ClickedAtUtc\", ");
        format.Append("\"CountryCode\", \"CountryName\", \"CityName\", \"PostalCode\", \"Latitude\", \"Longitude\", ");
        format.Append("\"DeviceFamily\", \"OperatingSystem\", \"OSVersion\", \"Browser\", \"BrowserVersion\"");
        format.Append(") VALUES ");

        var args = new List<object>(events.Count * 16);
        int pi = 0;
        for (int i = 0; i < events.Count; i++)
        {
            if (i > 0) format.Append(", ");
            format.Append($"({{{pi++}}}, {{{pi++}}}, {{{pi++}}}, {{{pi++}}}, {{{pi++}}}, ");
            format.Append($"{{{pi++}}}, {{{pi++}}}, {{{pi++}}}, {{{pi++}}}, {{{pi++}}}, {{{pi++}}}, ");
            format.Append($"{{{pi++}}}, {{{pi++}}}, {{{pi++}}}, {{{pi++}}}, {{{pi++}}})");

            var e = events[i];
            args.Add(e.ShortenedUrlId);
            args.Add(e.IpAddress);
            args.Add(e.UserAgent);
            args.Add(e.Referer);
            args.Add(e.ClickedAtUtc);
            args.Add((object?)e.CountryCode);
            args.Add((object?)e.CountryName);
            args.Add((object?)e.CityName);
            args.Add((object?)e.PostalCode);
            args.Add((object?)e.Latitude);
            args.Add((object?)e.Longitude);
            args.Add((object?)e.DeviceFamily);
            args.Add((object?)e.OperatingSystem);
            args.Add((object?)e.OSVersion);
            args.Add((object?)e.Browser);
            args.Add((object?)e.BrowserVersion);
        }

        return FormattableStringFactory.Create(format.ToString(), args.ToArray());
    }

    private void EnrichGeo(string ip, ClickEvent clickEvent)
    {
        if (!IPAddress.TryParse(ip, out var addr)) return;
        if (!_geoIp.TryCity(addr, out var city)) return;

        clickEvent.CountryCode = city.Country?.IsoCode;
        clickEvent.CountryName = city.Country?.Name;
        clickEvent.CityName = city.City?.Name;
        clickEvent.PostalCode = city.Postal?.Code;
        clickEvent.Latitude = city.Location?.Latitude;
        clickEvent.Longitude = city.Location?.Longitude;
    }

    private async Task DispatchWebhookEventsAsync(AppDbContext db, Dictionary<long, int> clickCountDelta, List<ClickRecord> buffer, DateTime batchTime, CancellationToken ct)
    {
        try
        {
            var linkIds = clickCountDelta.Keys.ToList();
            var links = await db.ShortenedUrls
                .AsNoTracking()
                .Where(l => linkIds.Contains(l.Id))
                .Include(l => l.Domain)
                .Include(l => l.Workspace)
                .ToListAsync(ct);

            var linkClicks = new Dictionary<long, (string ShortCode, string LongUrl, string? Domain, int ClickDelta, long TotalClicks)>();
            foreach (var link in links)
            {
                var clickDelta = clickCountDelta.GetValueOrDefault(link.Id, 0);
                if (clickDelta > 0)
                {
                    linkClicks[link.Id] = (
                        link.ShortCode,
                        link.LongUrl,
                        link.Domain?.Hostname,
                        clickDelta,
                        link.ClickCount);
                }
            }

            if (linkClicks.Count == 0) return;

            var ownerIds = links
                .Select(l => l.OwnerUserId ?? l.Workspace?.OwnerUserId)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .Distinct();

            var linkOwnerLookup = links.ToDictionary(
                l => l.Id,
                l => (Personal: l.OwnerUserId, Workspace: l.Workspace?.OwnerUserId));

            foreach (var ownerId in ownerIds)
            {
                var ownerLinkClicks = linkClicks
                    .Where(kvp => linkOwnerLookup.TryGetValue(kvp.Key, out var owner)
                        && (owner.Personal == ownerId || owner.Workspace == ownerId))
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

                await _webhookDispatcher.DispatchLinkClickedBatchAsync(
                    ownerId,
                    ownerLinkClicks,
                    batchTime.AddMinutes(-1),
                    batchTime,
                    "https",
                    "shortnr.example.com");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error dispatching webhook click events");
        }
    }
}