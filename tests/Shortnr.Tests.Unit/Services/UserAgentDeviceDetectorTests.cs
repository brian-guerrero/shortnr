namespace Shortnr.Tests.Unit.Services;

public class UserAgentDeviceDetectorTests
{
    private const string IosUa =
        "Mozilla/5.0 (iPhone; CPU iPhone OS 17_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Mobile/15E148 Safari/604.1";
    private const string AndroidUa =
        "Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Mobile Safari/537.36";
    private const string DesktopUa =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36";

    [Fact]
    public void GetMobilePlatform_IosUa_ReturnsIos() =>
        Assert.Equal(MobilePlatform.Ios, UserAgentDeviceDetector.GetMobilePlatform(IosUa));

    [Fact]
    public void GetMobilePlatform_AndroidUa_ReturnsAndroid() =>
        Assert.Equal(MobilePlatform.Android, UserAgentDeviceDetector.GetMobilePlatform(AndroidUa));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(DesktopUa)]
    public void GetMobilePlatform_NonMobile_ReturnsNone(string? ua) =>
        Assert.Equal(MobilePlatform.None, UserAgentDeviceDetector.GetMobilePlatform(ua));

    [Fact]
    public void ResolveDestination_IosUa_ReturnsIosDeepLink() =>
        Assert.Equal("myapp://open/123",
            UserAgentDeviceDetector.ResolveDestination("https://example.com/fallback", "myapp://open/123", null, IosUa));

    [Fact]
    public void ResolveDestination_AndroidUa_ReturnsAndroidDeepLink() =>
        Assert.Equal("https://play.google.com/store/apps/details?id=com.example.app",
            UserAgentDeviceDetector.ResolveDestination("https://example.com/fallback", null, "https://play.google.com/store/apps/details?id=com.example.app", AndroidUa));

    [Fact]
    public void ResolveDestination_DesktopUa_ReturnsFallback() =>
        Assert.Equal("https://example.com/fallback",
            UserAgentDeviceDetector.ResolveDestination("https://example.com/fallback", "myapp://open/123", "https://play.google.com/", DesktopUa));

    [Fact]
    public void ResolveDestination_IosUaWithoutIosLink_ReturnsFallback() =>
        Assert.Equal("https://example.com/fallback",
            UserAgentDeviceDetector.ResolveDestination("https://example.com/fallback", null, "https://play.google.com/", IosUa));

    [Fact]
    public void ResolveDestination_IosUaWithWhitespaceIosLink_ReturnsFallback() =>
        Assert.Equal("https://example.com/fallback",
            UserAgentDeviceDetector.ResolveDestination("https://example.com/fallback", "   ", null, IosUa));
}
