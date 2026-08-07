using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;

namespace Shortnr.Web.Features.AiActivity;

// Drains the queue of AI/MCP activity records written by MCP tools and inserts
// AiActivityLog rows. Kept off the request path so a tool call's audit entry
// never delays the response (mirrors ClickBatchProcessor / UserProvisioningProcessor).
public class AiActivityProcessor : BackgroundService
{
    private readonly Channel<AiActivityRecord> _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AiActivityProcessor> _logger;

    public AiActivityProcessor(Channel<AiActivityRecord> channel, IServiceScopeFactory scopeFactory,
        ILogger<AiActivityProcessor> logger)
    {
        _channel = channel;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AiActivityProcessor starting");
        var buffer = new List<AiActivityRecord>(100);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _channel.Reader.WaitToReadAsync(stoppingToken);
                if (stoppingToken.IsCancellationRequested) break;

                while (buffer.Count < 100 && _channel.Reader.TryRead(out var record))
                    buffer.Add(record);

                if (buffer.Count == 0) continue;

                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var ownerIds = buffer.Select(r => r.OwnerUserId).Distinct().ToList();
                var existingOwners = await db.Users
                    .Where(u => ownerIds.Contains(u.Id))
                    .Select(u => u.Id)
                    .ToHashSetAsync(stoppingToken);

                var rows = buffer
                    .Where(r => existingOwners.Contains(r.OwnerUserId))
                    .Select(r => new AiActivityLog
                    {
                        OwnerUserId = r.OwnerUserId,
                        ApiKeyId = r.ApiKeyId,
                        Action = r.Action,
                        TargetEntityType = r.TargetEntityType,
                        TargetEntityId = r.TargetEntityId,
                        Summary = r.Summary,
                        CreatedAtUtc = r.CreatedAtUtc
                    })
                    .ToList();

                if (rows.Count > 0)
                {
                    db.AiActivityLogs.AddRange(rows);
                    await db.SaveChangesAsync(stoppingToken);
                }

                _logger.LogInformation("Recorded {Count} AI activity entries", rows.Count);
                buffer.Clear();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error recording AI activity batch");
            }
        }

        _logger.LogInformation("AiActivityProcessor stopping");
    }
}
