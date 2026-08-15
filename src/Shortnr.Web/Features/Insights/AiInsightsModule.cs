namespace Shortnr.Web.Features.Insights;

using System.ClientModel;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OpenAI;
using Shortnr.Web.Features.Insights.Llm;

/// <summary>
/// PRD-006 AI Link Insights &amp; Auto-Tagging plus the PRD-023 LLM layer.
/// The deterministic background analysis stays off by default: when
/// <c>AiInsights:Enabled</c> is not true, no hosted service or analysis service is
/// registered and the /insights page returns 404.
/// <para>
/// The tiny LLM request-path services (<see cref="LlmInsightService"/> and friends)
/// are registered regardless so the Insights page model can be constructed even
/// when it's about to 404 — the page gates on <c>AiInsights:Llm:Enabled</c> at
/// request time and nothing executes while disabled.
/// </para>
/// </summary>
public static class AiInsightsModule
{
    public static IServiceCollection AddAiInsightsFeature(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AiInsightsOptions>(configuration.GetSection("AiInsights"));
        services.Configure<LlmOptions>(configuration.GetSection("AiInsights:Llm"));

        var llmConfig = configuration.GetSection("AiInsights:Llm").Get<LlmOptions>() ?? new LlmOptions();
        var timeoutSeconds = Math.Max(1, llmConfig.TimeoutSeconds);

        // Only actually exercised when Provider == Anthropic, but registered unconditionally
        // (like the AddSingleton<IChatClient> below) so the DI graph doesn't depend on config --
        // same "always register, gate at request time" shape the rest of this feature follows.
        services.AddHttpClient("llm-anthropic", c => c.Timeout = TimeSpan.FromSeconds(timeoutSeconds));

        services.AddSingleton<IChatClient>(sp =>
        {
            var inner = BuildInnerChatClient(llmConfig, sp, configuration);
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<LlmErrorTranslatingChatClient>();
            return new LlmErrorTranslatingChatClient(inner, logger);
        });

        services.AddScoped<LlmPricing>();
        services.AddScoped<LlmUsageService>();
        services.AddScoped<LlmInsightService>();

        if (!configuration.GetValue<bool>("AiInsights:Enabled", defaultValue: false))
            return services;

        services.AddScoped<AiInsightsService>();
        services.AddHostedService<AiInsightsHostedService>();

        return services;
    }

    /// <summary>
    /// Picks and constructs the right provider's <see cref="IChatClient"/> directly from
    /// <see cref="LlmOptions"/>. Clients are constructed directly (not through Aspire's
    /// <c>IHostApplicationBuilder</c>-extension DI helpers like <c>AddOllamaApiClient</c>) so
    /// this module keeps the same <c>(IServiceCollection, IConfiguration)</c> signature every
    /// other feature module uses, rather than needing the full host builder for one provider.
    /// </summary>
    private static IChatClient BuildInnerChatClient(LlmOptions cfg, IServiceProvider sp, IConfiguration configuration) => cfg.Provider switch
    {
        LlmProvider.OpenAi => BuildOpenAiCompatibleChatClient(cfg, "https://api.openai.com/v1"),
        LlmProvider.OpenRouter => BuildOpenAiCompatibleChatClient(cfg, "https://openrouter.ai/api/v1"),
        LlmProvider.Ollama => BuildOllamaChatClient(cfg, configuration),
        LlmProvider.Anthropic => new AnthropicChatClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("llm-anthropic"), cfg.ApiKey, cfg.BaseUrl),
        _ => throw new NotSupportedException($"Unknown LLM provider {cfg.Provider}")
    };

    // The LLM layer is registered unconditionally (see class doc) even when unconfigured --
    // LlmOptions.ApiKey/Model default to "" in the committed appsettings.json, and both the
    // OpenAI SDK's ApiKeyCredential and GetChatClient(model) throw ArgumentException on an
    // empty string. LlmInsightService already gates on IsEnabled/Model before ever calling
    // this client (see NotConfigured/Disabled short-circuits), so a placeholder here is only
    // ever used to keep DI construction from throwing while genuinely unconfigured.
    private const string UnconfiguredPlaceholder = "unset";

    private static IChatClient BuildOpenAiCompatibleChatClient(LlmOptions cfg, string defaultEndpoint)
    {
        var endpoint = string.IsNullOrEmpty(cfg.BaseUrl) ? defaultEndpoint : cfg.BaseUrl.TrimEnd('/');
        var apiKey = string.IsNullOrEmpty(cfg.ApiKey) ? UnconfiguredPlaceholder : cfg.ApiKey;
        var model = string.IsNullOrEmpty(cfg.Model) ? UnconfiguredPlaceholder : cfg.Model;
        var client = new OpenAIClient(new ApiKeyCredential(apiKey), new OpenAIClientOptions { Endpoint = new Uri(endpoint) });
        return client.GetChatClient(model).AsIChatClient();
    }

    /// <summary>
    /// Prefers the Aspire-injected connection string (set via <c>.WithReference(ollamaModel)</c>
    /// in the AppHost, resource named "llm-ollama" to match) so `dotnet run --project
    /// src\Shortnr.AppHost` needs zero manual BaseUrl/Model configuration; falls back to
    /// <see cref="LlmOptions.BaseUrl"/>/<see cref="LlmOptions.Model"/> for a manually-run Ollama
    /// instance outside Aspire.
    /// </summary>
    private static IChatClient BuildOllamaChatClient(LlmOptions cfg, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("llm-ollama");
        var (endpoint, model) = connectionString is not null
            ? ParseOllamaConnectionString(connectionString, cfg)
            : (string.IsNullOrEmpty(cfg.BaseUrl) ? "http://localhost:11434" : cfg.BaseUrl.TrimEnd('/'), cfg.Model);

        return new OllamaApiClient(new Uri(endpoint), string.IsNullOrEmpty(model) ? UnconfiguredPlaceholder : model);
    }

    private static (string Endpoint, string Model) ParseOllamaConnectionString(string connectionString, LlmOptions cfg)
    {
        string? endpoint = null;
        string? model = null;
        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length != 2) continue;
            if (pair[0].Equals("Endpoint", StringComparison.OrdinalIgnoreCase)) endpoint = pair[1];
            else if (pair[0].Equals("Model", StringComparison.OrdinalIgnoreCase)) model = pair[1];
        }
        return (endpoint ?? "http://localhost:11434", model ?? cfg.Model);
    }
}
