using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Web.Models;

namespace Shortnr.Web.Services;

public class ClickBatchProcessor : BackgroundService
{
    private readonly Channel<ClickRecord> _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ClickBatchProcessor> _logger;

    public ClickBatchProcessor(Channel<ClickRecord> channel, IServiceScopeFactory scopeFactory, ILogger<ClickBatchProcessor> logger)
    {
        _channel = channel;
        _scopeFactory = scopeFactory;
        _logger = logger;
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
                    db.ClickEvents.Add(new ClickEvent
                    {
                        ShortenedUrlId = url.Id,
                        IpAddress = record.IpAddress,
                        UserAgent = record.UserAgent,
                        Referer = record.Referer,
                        ClickedAtUtc = DateTime.UtcNow
                    });
                }

                await db.SaveChangesAsync(stoppingToken);
                _logger.LogInformation("Processed {Count} click events in one batch", buffer.Count);
                buffer.Clear();
            }
            catch (OperationCanceledException)
            {
                // Expected when stoppingToken is cancelled
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing click batch");
            }
        }

        _logger.LogInformation("ClickBatchProcessor stopping");
    }
}
