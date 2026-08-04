using Shortnr.Cli.Services;

namespace Shortnr.Tests.Unit.Cli;

public class ConfigLoaderTests
{
    [Fact]
    public void Load_WithEnvVar_ReturnsEnvVarValue()
    {
        var originalKey = Environment.GetEnvironmentVariable(ConfigLoader.EnvVarApiKey);
        var originalUrl = Environment.GetEnvironmentVariable(ConfigLoader.EnvVarBaseUrl);

        try
        {
            Environment.SetEnvironmentVariable(ConfigLoader.EnvVarApiKey, "snr_test_key");
            Environment.SetEnvironmentVariable(ConfigLoader.EnvVarBaseUrl, "https://test.example.com");

            var config = ConfigLoader.Load();

            Assert.Equal("snr_test_key", config.ApiKey);
            Assert.Equal("https://test.example.com", config.BaseUrl);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ConfigLoader.EnvVarApiKey, originalKey);
            Environment.SetEnvironmentVariable(ConfigLoader.EnvVarBaseUrl, originalUrl);
        }
    }

    [Fact]
    public void Load_WithNoEnvVar_ReturnsDefaultBaseUrl()
    {
        var originalKey = Environment.GetEnvironmentVariable(ConfigLoader.EnvVarApiKey);
        var originalUrl = Environment.GetEnvironmentVariable(ConfigLoader.EnvVarBaseUrl);

        try
        {
            Environment.SetEnvironmentVariable(ConfigLoader.EnvVarApiKey, null);
            Environment.SetEnvironmentVariable(ConfigLoader.EnvVarBaseUrl, null);

            var config = ConfigLoader.Load();

            Assert.Null(config.ApiKey);
            Assert.Equal(ConfigLoader.DefaultBaseUrl, config.BaseUrl);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ConfigLoader.EnvVarApiKey, originalKey);
            Environment.SetEnvironmentVariable(ConfigLoader.EnvVarBaseUrl, originalUrl);
        }
    }

    [Fact]
    public void HasApiKey_WithKey_ReturnsTrue()
    {
        var config = new CliConfig("snr_test", "http://localhost");
        Assert.True(ConfigLoader.HasApiKey(config));
    }

    [Fact]
    public void HasApiKey_WithNull_ReturnsFalse()
    {
        var config = new CliConfig(null, "http://localhost");
        Assert.False(ConfigLoader.HasApiKey(config));
    }

    [Fact]
    public void HasApiKey_WithEmpty_ReturnsFalse()
    {
        var config = new CliConfig("", "http://localhost");
        Assert.False(ConfigLoader.HasApiKey(config));
    }

    [Fact]
    public void HasApiKey_WithWhitespace_ReturnsFalse()
    {
        var config = new CliConfig("   ", "http://localhost");
        Assert.False(ConfigLoader.HasApiKey(config));
    }

    [Fact]
    public void GetConfigFilePath_ReturnsValidPath()
    {
        var path = ConfigLoader.GetConfigFilePath();
        Assert.Contains(ConfigLoader.ConfigDir, path);
        Assert.Contains(ConfigLoader.ConfigFile, path);
    }
}
