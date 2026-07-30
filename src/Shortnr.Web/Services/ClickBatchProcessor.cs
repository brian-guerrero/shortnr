using System.Net;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Web.Models;
using uaParserLibrary;

namespace Shortnr.Web.Services;

public class ClickBatchProcessor : BackgroundService
{
    private readonly Channel<ClickRecord> _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ClickBatchProcessor> _logger;
    private readonly GeoIpService _geoIp;
    private readonly Channel<object> _sseChannel;

    public ClickBatchProcessor(Channel<ClickRecord> channel, IServiceScopeFactory scopeFactory,
        ILogger<ClickBatchProcessor> logger, GeoIpService geoIp, Channel<object> sseChannel)
    {
        _channel = channel;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _geoIp = geoIp;
        _sseChannel = sseChannel;
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
                    _logger.LogTrace("Clicked recorded from {Ip} {ShortCode}", record.IpAddress, record.ShortCode);
                }

                if (buffer.Count == 0) continue;

                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var shortCodes = buffer.Select(r => r.ShortCode).Distinct().ToList();
                var urls = await db.ShortenedUrls
                    .Where(u => shortCodes.Contains(u.ShortCode))
                    .ToListAsync(stoppingToken);

                var urlDict = urls.ToDictionary(u => u.ShortCode);

                foreach (var record in buffer)
                {
                    if (!urlDict.TryGetValue(record.ShortCode, out var url)) continue;

                    url.ClickCount++;

                    var uaInfo = uaParserLibrary.UAParser.GetClientInfo(record.UserAgent);

                    var clickEvent = new ClickEvent
                    {
                        ShortenedUrlId = url.Id,
                        IpAddress = record.IpAddress,
                        UserAgent = record.UserAgent,
                        Referer = record.Referer,
                        ClickedAtUtc = DateTime.UtcNow,
                        DeviceFamily = uaInfo.Device.Type ?? uaInfo.Device.Model,
                        OperatingSystem = uaInfo.OS.Name,
                        OSVersion = uaInfo.OS.Version,
                        Browser = uaInfo.Browser.Name,
                        BrowserVersion = uaInfo.Browser.Major is not null
                            ? uaInfo.Browser.Major + (uaInfo.Browser.Version is not null ? "." + uaInfo.Browser.Version : "")
                            : uaInfo.Browser.Version
                    };

                    EnrichGeo(record.IpAddress, clickEvent);

                    db.ClickEvents.Add(clickEvent);
                }

                await db.SaveChangesAsync(stoppingToken);
                _logger.LogInformation("Processed {Count} click events in one batch — notifying SSE clients", buffer.Count);
                _sseChannel.Writer.TryWrite(new object());
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
}
