using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Web.Models;

namespace Shortnr.Web.Services;

public class ClickBatchProcessor : BackgroundService
{
    private readonly Channel<ClickRecord> _channel;
    private readonly IServiceScopeFactory _scopeFactory;

    public ClickBatchProcessor(Channel<ClickRecord> channel, IServiceScopeFactory scopeFactory)
    {
        _channel = channel;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var buffer = new List<ClickRecord>(100);

        while (!stoppingToken.IsCancellationRequested)
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
            buffer.Clear();
        }
    }
}
