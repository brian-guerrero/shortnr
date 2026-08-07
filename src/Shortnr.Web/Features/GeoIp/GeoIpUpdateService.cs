using System.Formats.Tar;
using System.IO.Compression;

namespace Shortnr.Web.Features.GeoIp;

public class GeoIpUpdateService : BackgroundService
{
    private static readonly string DownloadUrl =
        "https://download.maxmind.com/app/geoip_download?edition_id=GeoLite2-City&license_key={0}&suffix=tar.gz";

    private readonly string _databasePath;
    private readonly string _licenseKey;
    private readonly bool _enabled;
    private readonly ILogger<GeoIpUpdateService> _logger;
    private readonly GeoIpService _geoIpService;
    private readonly HttpClient _httpClient;

    public GeoIpUpdateService(IHttpClientFactory httpClientFactory, IConfiguration config,
        ILogger<GeoIpUpdateService> logger, GeoIpService geoIpService, IWebHostEnvironment env)
    {
        var configuredPath = config["GeoIp:DatabasePath"];
        _databasePath = !string.IsNullOrWhiteSpace(configuredPath)
            ? configuredPath
            : Path.Combine(env.WebRootPath, "data", "GeoLite2-City.mmdb");
        _logger = logger;
        _geoIpService = geoIpService;

        var accountId = config["GeoIp:MaxMindAccountId"] ?? string.Empty;
        _licenseKey = config["GeoIp:MaxMindLicenseKey"] ?? string.Empty;
        _enabled = !string.IsNullOrWhiteSpace(accountId) && !string.IsNullOrWhiteSpace(_licenseKey);

        _httpClient = httpClientFactory.CreateClient("GeoIpUpdate");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            _logger.LogWarning(
                "GeoIP enrichment is disabled: no GeoIp:MaxMindAccountId / GeoIp:MaxMindLicenseKey configured. " +
                "Set both to download GeoLite2-City from MaxMind's official endpoint. " +
                "Geo enrichment degrades gracefully to no geo data.");
            return;
        }

        if (ShouldDownload())
            await DownloadAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeUntilNextScheduledRun();
            _logger.LogInformation("Next GeoIP update scheduled in {Hours}h {Minutes}m", delay.Hours, delay.Minutes);
            await Task.Delay(delay, stoppingToken);
            await DownloadAsync(stoppingToken);
        }
    }

    private static TimeSpan TimeUntilNextScheduledRun()
    {
        var now = DateTime.UtcNow;
        var today = now.Date;

        // Target days: Wednesday (3) and Saturday (6)
        var todayDow = (int)today.DayOfWeek;

        int daysUntil;
        if (todayDow == 3 || todayDow == 6)
        {
            var nextTarget = todayDow == 3 ? 6 : 3 + 7;
            if (now.TimeOfDay < TimeSpan.FromHours(12))
            {
                // Run today at 12:00 UTC
                daysUntil = 0;
            }
            else
            {
                daysUntil = nextTarget - todayDow;
            }
        }
        else
        {
            int[] nextTargets = [3, 6];
            daysUntil = nextTargets
                .Select(d => d >= todayDow ? d - todayDow : d + 7 - todayDow)
                .Min();
        }

        var nextRun = today.AddDays(daysUntil).AddHours(12);
        return nextRun - now;
    }

    private bool ShouldDownload()
    {
        if (!File.Exists(_databasePath))
            return true;

        var lastWrite = File.GetLastWriteTimeUtc(_databasePath);
        return DateTime.UtcNow - lastWrite > TimeSpan.FromDays(3);
    }

    private async Task DownloadAsync(CancellationToken ct)
    {
        var url = string.Format(DownloadUrl, _licenseKey);
        var tempDir = Path.Combine(Path.GetTempPath(), "shortnr-geoip-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(tempDir);

            _logger.LogInformation("Downloading GeoIP database from MaxMind");
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var archivePath = Path.Combine(tempDir, "GeoLite2-City.tar.gz");
            await using (var file = File.Create(archivePath))
            {
                await response.Content.CopyToAsync(file, ct);
            }

            // MaxMind ships the database as a .tar.gz containing a dated folder
            // (e.g. GeoLite2-City_20260728/GeoLite2-City.mmdb). Extract and locate
            // the inner .mmdb file rather than assuming a fixed layout.
            TarFile.ExtractToDirectory(archivePath, tempDir, overwriteFiles: true);
            var mmdbPath = Directory
                .EnumerateFiles(tempDir, "GeoLite2-City.mmdb", SearchOption.AllDirectories)
                .FirstOrDefault()
                ?? throw new InvalidDataException("MaxMind archive contained no GeoLite2-City.mmdb file.");

            var dir = Path.GetDirectoryName(_databasePath);
            if (dir is not null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var tempPath = _databasePath + ".tmp";
            File.Copy(mmdbPath, tempPath, overwrite: true);
            File.Move(tempPath, _databasePath, overwrite: true);

            _logger.LogInformation("GeoIP database downloaded and extracted to {Path}", _databasePath);
            _geoIpService.Reload();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download GeoIP database from MaxMind");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); }
            catch (IOException) { }
        }
    }
}
