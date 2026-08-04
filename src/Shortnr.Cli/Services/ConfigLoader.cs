using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shortnr.Cli.Services;

[JsonSerializable(typeof(CliConfig))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public partial class ConfigJsonContext : JsonSerializerContext
{
}

public record CliConfig(
    [property: JsonPropertyName("api_key")] string? ApiKey,
    [property: JsonPropertyName("base_url")] string? BaseUrl);

public static class ConfigLoader
{
    public const string DefaultBaseUrl = "http://localhost:5156";
    public const string EnvVarApiKey = "SHORTNR_API_KEY";
    public const string EnvVarBaseUrl = "SHORTNR_BASE_URL";
    public const string ConfigDir = ".shortnr";
    public const string ConfigFile = "config";

    public static CliConfig Load()
    {
        var envApiKey = Environment.GetEnvironmentVariable(EnvVarApiKey);
        var envBaseUrl = Environment.GetEnvironmentVariable(EnvVarBaseUrl);

        CliConfig? fileConfig = null;
        var configPath = GetConfigFilePath();
        if (File.Exists(configPath))
        {
            try
            {
                var json = File.ReadAllText(configPath);
                fileConfig = JsonSerializer.Deserialize(json, ConfigJsonContext.Default.CliConfig);
            }
            catch
            {
            }
        }

        var apiKey = envApiKey ?? fileConfig?.ApiKey;
        var baseUrl = envBaseUrl ?? fileConfig?.BaseUrl ?? DefaultBaseUrl;

        return new CliConfig(apiKey, baseUrl?.TrimEnd('/'));
    }

    public static string GetConfigFilePath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ConfigDir, ConfigFile);
    }

    public static bool HasApiKey(CliConfig config) =>
        !string.IsNullOrWhiteSpace(config.ApiKey);
}
