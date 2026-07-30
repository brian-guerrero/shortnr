using Microsoft.Extensions.Logging.Abstractions;
using Shortnr.Web.Services;

namespace Shortnr.Tests.Unit.Services;

public class GeoIpServiceTests
{
    [Fact]
    public void Constructor_WhenDatabaseMissing_IsAvailableFalse()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "nonexistent.mmdb");
        var sut = new GeoIpService(path, NullLogger<GeoIpService>.Instance);

        Assert.False(sut.IsAvailable);
    }

    [Fact]
    public void TryCity_WhenNotAvailable_ReturnsFalse()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "nonexistent.mmdb");
        var sut = new GeoIpService(path, NullLogger<GeoIpService>.Instance);

        var result = sut.TryCity(System.Net.IPAddress.Parse("8.8.8.8"), out var response);

        Assert.False(result);
        Assert.Null(response);
    }
}
