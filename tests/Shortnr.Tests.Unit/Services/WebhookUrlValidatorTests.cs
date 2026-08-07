using System.Net;

namespace Shortnr.Tests.Unit.Services;

public class WebhookUrlValidatorTests
{
    [Theory]
    [InlineData("https://example.com/webhook")]
    [InlineData("http://example.com/webhook")]
    [InlineData("https://subdomain.example.com/webhook")]
    [InlineData("https://example.com:8080/webhook")]
    public void Validate_ValidPublicUrls_ReturnsTrue(string url)
    {
        var (isValid, error) = WebhookUrlValidator.Validate(url);

        Assert.True(isValid);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyUrls_ReturnsFalse(string url)
    {
        var (isValid, error) = WebhookUrlValidator.Validate(url);

        Assert.False(isValid);
        Assert.Equal("URL is required.", error);
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("/relative/path")]
    public void Validate_InvalidUrls_ReturnsFalse(string url)
    {
        var (isValid, error) = WebhookUrlValidator.Validate(url);

        Assert.False(isValid);
        Assert.Equal("URL must be an absolute URI.", error);
    }

    [Theory]
    [InlineData("ftp://example.com/webhook")]
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    public void Validate_NonHttpSchemes_ReturnsFalse(string url)
    {
        var (isValid, error) = WebhookUrlValidator.Validate(url);

        Assert.False(isValid);
        Assert.Equal("Only http and https schemes are allowed.", error);
    }

    [Theory]
    [InlineData("http://localhost/webhook")]
    [InlineData("https://localhost/webhook")]
    [InlineData("http://LOCALHOST/webhook")]
    public void Validate_Localhost_ReturnsFalse(string url)
    {
        var (isValid, error) = WebhookUrlValidator.Validate(url);

        Assert.False(isValid);
        Assert.Equal("localhost is not allowed.", error);
    }

    [Theory]
    [InlineData("http://service.local/webhook")]
    [InlineData("https://internal.local/webhook")]
    public void Validate_LocalDomains_ReturnsFalse(string url)
    {
        var (isValid, error) = WebhookUrlValidator.Validate(url);

        Assert.False(isValid);
        Assert.Equal(".local domains are not allowed.", error);
    }

    [Theory]
    [InlineData("http://service.internal/webhook")]
    [InlineData("https://api.internal/webhook")]
    public void Validate_InternalDomains_ReturnsFalse(string url)
    {
        var (isValid, error) = WebhookUrlValidator.Validate(url);

        Assert.False(isValid);
        Assert.Equal(".internal domains are not allowed.", error);
    }

    [Theory]
    [InlineData("http://127.0.0.1/webhook")]
    [InlineData("https://127.0.0.1/webhook")]
    [InlineData("http://127.255.255.255/webhook")]
    public void Validate_LoopbackIPs_ReturnsFalse(string url)
    {
        var (isValid, error) = WebhookUrlValidator.Validate(url);

        Assert.False(isValid);
        Assert.Equal("Private and internal IP addresses are not allowed.", error);
    }

    [Theory]
    [InlineData("http://10.0.0.1/webhook")]
    [InlineData("https://10.255.255.255/webhook")]
    [InlineData("http://10.0.0.0/webhook")]
    public void Validate_ClassAPrivateIPs_ReturnsFalse(string url)
    {
        var (isValid, error) = WebhookUrlValidator.Validate(url);

        Assert.False(isValid);
        Assert.Equal("Private and internal IP addresses are not allowed.", error);
    }

    [Theory]
    [InlineData("http://172.16.0.1/webhook")]
    [InlineData("https://172.31.255.255/webhook")]
    [InlineData("http://172.20.0.1/webhook")]
    public void Validate_ClassBPrivateIPs_ReturnsFalse(string url)
    {
        var (isValid, error) = WebhookUrlValidator.Validate(url);

        Assert.False(isValid);
        Assert.Equal("Private and internal IP addresses are not allowed.", error);
    }

    [Theory]
    [InlineData("http://192.168.0.1/webhook")]
    [InlineData("https://192.168.255.255/webhook")]
    [InlineData("http://192.168.1.1/webhook")]
    public void Validate_ClassCPrivateIPs_ReturnsFalse(string url)
    {
        var (isValid, error) = WebhookUrlValidator.Validate(url);

        Assert.False(isValid);
        Assert.Equal("Private and internal IP addresses are not allowed.", error);
    }

    [Theory]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("https://169.254.0.1/webhook")]
    public void Validate_LinkLocalIPs_ReturnsFalse(string url)
    {
        var (isValid, error) = WebhookUrlValidator.Validate(url);

        Assert.False(isValid);
        Assert.Equal("Private and internal IP addresses are not allowed.", error);
    }

    [Theory]
    [InlineData("http://0.0.0.0/webhook")]
    [InlineData("https://0.0.0.0/webhook")]
    public void Validate_UnspecifiedIPs_ReturnsFalse(string url)
    {
        var (isValid, error) = WebhookUrlValidator.Validate(url);

        Assert.False(isValid);
        Assert.Equal("Private and internal IP addresses are not allowed.", error);
    }

    [Fact]
    public void IsPrivateIp_LoopbackIPv4_ReturnsTrue()
    {
        Assert.True(WebhookUrlValidator.IsPrivateIp(IPAddress.Parse("127.0.0.1")));
        Assert.True(WebhookUrlValidator.IsPrivateIp(IPAddress.Parse("127.255.255.255")));
    }

    [Fact]
    public void IsPrivateIp_LoopbackIPv6_ReturnsTrue()
    {
        Assert.True(WebhookUrlValidator.IsPrivateIp(IPAddress.Parse("::1")));
    }

    [Fact]
    public void IsPrivateIp_PrivateIPv4_ReturnsTrue()
    {
        Assert.True(WebhookUrlValidator.IsPrivateIp(IPAddress.Parse("10.0.0.1")));
        Assert.True(WebhookUrlValidator.IsPrivateIp(IPAddress.Parse("172.16.0.1")));
        Assert.True(WebhookUrlValidator.IsPrivateIp(IPAddress.Parse("192.168.0.1")));
    }

    [Fact]
    public void IsPrivateIp_PublicIPv4_ReturnsFalse()
    {
        Assert.False(WebhookUrlValidator.IsPrivateIp(IPAddress.Parse("8.8.8.8")));
        Assert.False(WebhookUrlValidator.IsPrivateIp(IPAddress.Parse("1.1.1.1")));
        Assert.False(WebhookUrlValidator.IsPrivateIp(IPAddress.Parse("93.184.216.34")));
    }

    [Fact]
    public void IsPrivateIp_LinkLocalIPv4_ReturnsTrue()
    {
        Assert.True(WebhookUrlValidator.IsPrivateIp(IPAddress.Parse("169.254.169.254")));
        Assert.True(WebhookUrlValidator.IsPrivateIp(IPAddress.Parse("169.254.0.1")));
    }

    [Fact]
    public void IsPrivateIp_IPv6LinkLocal_ReturnsTrue()
    {
        Assert.True(WebhookUrlValidator.IsPrivateIp(IPAddress.Parse("fe80::1")));
        Assert.True(WebhookUrlValidator.IsPrivateIp(IPAddress.Parse("fe80::1234:5678")));
    }
}
