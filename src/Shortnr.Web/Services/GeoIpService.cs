using System.Diagnostics.CodeAnalysis;
using System.Net;
using MaxMind.GeoIP2;
using MaxMind.GeoIP2.Responses;

namespace Shortnr.Web.Services;

public class GeoIpService : IDisposable
{
    private DatabaseReader? _reader;
    private readonly string _databasePath;
    private readonly ILogger<GeoIpService> _logger;
    private readonly object _lock = new();

    public GeoIpService(string databasePath, ILogger<GeoIpService> logger)
    {
        _databasePath = databasePath;
        _logger = logger;
        TryOpenReader();
    }

    public bool IsAvailable => _reader is not null;

    public bool TryCity(IPAddress address, [NotNullWhen(true)] out CityResponse? response)
    {
        response = null;
        DatabaseReader? reader;
        lock (_lock) { reader = _reader; }

        if (reader is null) return false;
        return reader.TryCity(address, out response);
    }

    public void Reload()
    {
        lock (_lock)
        {
            _reader?.Dispose();
            _reader = null;
            TryOpenReader();
        }
    }

    private void TryOpenReader()
    {
        try
        {
            if (File.Exists(_databasePath))
            {
                _reader = new DatabaseReader(_databasePath);
                _logger.LogInformation("GeoIP database loaded from {Path}", _databasePath);
            }
            else
            {
                _logger.LogWarning("GeoIP database not found at {Path}. Geo enrichment will be skipped.", _databasePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load GeoIP database from {Path}", _databasePath);
        }
    }

    public void Dispose()
    {
        _reader?.Dispose();
    }
}
