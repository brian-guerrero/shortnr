using Microsoft.EntityFrameworkCore;
using Shortnr.Data;

namespace Shortnr.Web.Features.Theming;

/// <summary>
/// At startup, ensures every community theme currently selected somewhere — a
/// bio page's theme, a link's preview theme, or a workspace's default preview
/// theme — is actually installed (cached to disk). This is what makes
/// community themes survive a redeploy onto a volume that isn't persistent:
/// without it, a theme that was already selected before the restart would
/// otherwise sit unstyled until the next time something happens to resolve it
/// (see <see cref="ThemeResolver.FindAsync"/>, which installs on the fly too,
/// but only reacts to a read — this proactively reconciles right away).
/// <para>
/// Mirrors <c>GeoIpUpdateService</c>'s download-on-start, fail-open-and-log
/// idiom, but runs once rather than on a recurring schedule — a reconciliation
/// pass only matters right after the process (re)starts; DB rows don't change
/// out from under a running process the way a several-day-old GeoIP database
/// file does.
/// </para>
/// </summary>
public sealed class CommunityThemeInstallerService(
    IServiceScopeFactory scopeFactory,
    ICommunityThemeCatalog catalog,
    ILogger<CommunityThemeInstallerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var inUseIds = await CollectInUseThemeIdsAsync(db, stoppingToken);
            var communityIds = inUseIds
                .Where(id => !ThemeCatalog.IsValid(id))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (communityIds.Length == 0) return;

            logger.LogInformation("Ensuring {Count} in-use community theme(s) are installed", communityIds.Length);
            foreach (var id in communityIds)
            {
                var css = await catalog.GetCssAsync(id, stoppingToken);
                if (css is null)
                    logger.LogWarning("Community theme {ThemeId} is selected but could not be installed", id);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Best-effort reconciliation — a failure here should never take
            // the host down. Anything missed here still gets a second chance
            // the next time it's actually resolved (ThemeResolver.FindAsync).
            logger.LogWarning(ex, "Community theme install check failed at startup");
        }
    }

    private static async Task<IReadOnlyList<string>> CollectInUseThemeIdsAsync(AppDbContext db, CancellationToken ct)
    {
        var bioThemes = await db.BioPages.Select(b => b.Theme).ToListAsync(ct);
        var linkThemes = await db.ShortenedUrls
            .Where(l => l.PreviewTheme != null)
            .Select(l => l.PreviewTheme!)
            .ToListAsync(ct);
        var workspaceThemes = await db.Workspaces
            .Where(w => w.DefaultPreviewTheme != null)
            .Select(w => w.DefaultPreviewTheme!)
            .ToListAsync(ct);

        return [.. bioThemes, .. linkThemes, .. workspaceThemes];
    }
}
