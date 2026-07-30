using System.IO.Compression;

namespace Shortnr.Web.Services;

public class GeoIpUpdateService : BackgroundService
{
    private static readonly Uri DownloadUrl = new("https://cdn.jsdelivr.net/npm/geolite2-city/GeoLite2-City.mmdb.gz");

    private readonly string _databasePath;
    private readonly ILogger<GeoIpUpdateService> _logger;
    private readonly GeoIpService _geoIpService;
    private readonly HttpClient _httpClient;

    public GeoIpUpdateService(IConfiguration config, ILogger<GeoIpUpdateService> logger,
        GeoIpService geoIpService, IWebHostEnvironment env)
    {
        var configuredPath = config["GeoIp:DatabasePath"];
        _databasePath = !string.IsNullOrWhiteSpace(configuredPath)
            ? configuredPath
            : Path.Combine(env.WebRootPath, "data", "GeoLite2-City.mmdb");
        _logger = logger;
        _geoIpService = geoIpService;
        _httpClient = new HttpClient();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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

    public override void Dispose()
    {
        _httpClient.Dispose();
        base.Dispose();
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
        var dir = Path.GetDirectoryName(_databasePath);
        if (dir is not null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        try
        {
            _logger.LogInformation("Downloading GeoIP database from {Url}", DownloadUrl);
            using var response = await _httpClient.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var tempPath = _databasePath + ".tmp";
            await using var compressed = await response.Content.ReadAsStreamAsync(ct);
            await using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
            {
                await using var file = File.Create(tempPath);
                await gzip.CopyToAsync(file, ct);
            }

            File.Move(tempPath, _databasePath, overwrite: true);

            _logger.LogInformation("GeoIP database downloaded and extracted to {Path}", _databasePath);
            _geoIpService.Reload();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download GeoIP database from {Url}", DownloadUrl);
        }
    }
}
