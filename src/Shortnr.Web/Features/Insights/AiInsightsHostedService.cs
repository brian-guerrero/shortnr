using Microsoft.Extensions.Options;

namespace Shortnr.Web.Features.Insights;

/// <summary>
/// Runs the AI insights analysis pass on a timer (default every 24h). The first
/// pass waits a full interval before running so the job never races the host
/// during startup or tests. Mirrors the Channel-based background processors but
/// is timer-driven because there is no request path feeding it work.
/// </summary>
public class AiInsightsHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<AiInsightsOptions> options,
    ILogger<AiInsightsHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromHours(Math.Max(1, options.Value.AnalysisIntervalHours));
        logger.LogInformation("AiInsightsHostedService starting; analysis every {Hours}h", interval.TotalHours);

        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<AiInsightsService>();
                var created = await service.RunAnalysisAsync(stoppingToken);
                logger.LogInformation("AI insights analysis complete: {Count} suggestions created", created);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "AI insights analysis pass failed");
            }
        }

        logger.LogInformation("AiInsightsHostedService stopping");
    }
}
