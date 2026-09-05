using Microsoft.Extensions.Options;
using Shortnr.Data.Entities;

namespace Shortnr.Web.Features.Social;

/// <summary>
/// Background service that periodically refreshes expiring social-account OAuth tokens.
/// Mirrors the <see cref="AiInsightsHostedService"/> pattern: PeriodicTimer-based,
/// config-gated, resolves scoped services via <see cref="IServiceScopeFactory"/>.
/// Runs every <see cref="SocialTokenRefreshOptions.IntervalHours"/> hours (default 6).
/// </summary>
public sealed class SocialTokenRefreshHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<SocialTokenRefreshOptions> options,
    IEnumerable<ISocialPlatformProvider> providers,
    ILogger<SocialTokenRefreshHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var cfg = options.Value;
        if (!cfg.Enabled)
        {
            logger.LogInformation("SocialTokenRefreshHostedService disabled via config");
            return;
        }

        var interval = TimeSpan.FromHours(Math.Max(1, cfg.IntervalHours));
        logger.LogInformation("SocialTokenRefreshHostedService starting; refresh every {Hours}h, window {WindowHours}h",
            interval.TotalHours, cfg.RefreshWindowHours);

        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<SocialAccountService>();
                var expiring = await service.GetExpiringAsync(cfg.RefreshWindowHours, stoppingToken);

                if (expiring.Count == 0)
                {
                    logger.LogDebug("No social accounts need token refresh");
                    continue;
                }

                logger.LogInformation("Refreshing tokens for {Count} social accounts", expiring.Count);

                foreach (var account in expiring)
                {
                    try
                    {
                        var provider = providers.FirstOrDefault(p => p.Platform == account.Platform);
                        if (provider is null)
                        {
                            logger.LogWarning("No provider registered for platform {Platform}", account.Platform);
                            continue;
                        }

                        // The account's tokens are still encrypted here — decrypt for the provider.
                        var decrypted = new SocialAccount
                        {
                            Id = account.Id,
                            PlatformAccountId = account.PlatformAccountId,
                            Platform = account.Platform,
                            RefreshTokenEncrypted = account.RefreshTokenEncrypted,
                            AccessTokenExpiryUtc = account.AccessTokenExpiryUtc,
                        };

                        var result = await provider.RefreshTokenAsync(decrypted, stoppingToken);
                        if (result is null)
                        {
                            logger.LogWarning("Token refresh failed for {Platform} account {AccountId}",
                                account.Platform, account.PlatformAccountId);
                            await service.MarkRefreshFailedAsync(account.Id, stoppingToken);
                            continue;
                        }

                        await service.UpdateTokensAfterRefreshAsync(
                            account.Id,
                            result.AccessToken,
                            result.RefreshToken,
                            result.AccessTokenExpiryUtc,
                            result.RefreshTokenExpiryUtc,
                            stoppingToken);

                        logger.LogInformation("Token refreshed for {Platform} account {AccountId}",
                            account.Platform, account.PlatformAccountId);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Token refresh failed for {Platform} account {AccountId}",
                            account.Platform, account.PlatformAccountId);
                        await service.MarkRefreshFailedAsync(account.Id, stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Social token refresh scheduler pass failed");
            }
        }

        logger.LogInformation("SocialTokenRefreshHostedService stopping");
    }
}
