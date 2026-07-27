using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;

namespace Shortnr.Web.Services;

public class ClickBatchProcessor : BackgroundService
{
    private readonly Channel<string> _channel;
    private readonly IServiceScopeFactory _scopeFactory;

    public ClickBatchProcessor(Channel<string> channel, IServiceScopeFactory scopeFactory)
    {
        _channel = channel;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var buffer = new List<string>(100);

        while (!stoppingToken.IsCancellationRequested)
        {
            await _channel.Reader.WaitToReadAsync(stoppingToken);
            if (stoppingToken.IsCancellationRequested) break;

            while (buffer.Count < 100 && _channel.Reader.TryRead(out var shortCode))
            {
                buffer.Add(shortCode);
            }

            if (buffer.Count == 0) continue;

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            foreach (var code in buffer)
            {
                var link = await db.ShortenedUrls.FirstOrDefaultAsync(l => l.ShortCode == code, stoppingToken);
                if (link is not null) link.ClickCount++;
            }

            await db.SaveChangesAsync(stoppingToken);
            buffer.Clear();
        }
    }
}
