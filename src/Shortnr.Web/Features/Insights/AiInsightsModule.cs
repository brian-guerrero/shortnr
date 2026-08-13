namespace Shortnr.Web.Features.Insights;

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

        var timeoutSeconds = configuration.GetValue<int>("AiInsights:Llm:TimeoutSeconds", defaultValue: 60);
        services.AddHttpClient<ILlmClient, LlmClient>(client =>
            client.Timeout = TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)));
        services.AddScoped<LlmPricing>();
        services.AddScoped<LlmUsageService>();
        services.AddScoped<LlmInsightService>();

        if (!configuration.GetValue<bool>("AiInsights:Enabled", defaultValue: false))
            return services;

        services.AddScoped<AiInsightsService>();
        services.AddHostedService<AiInsightsHostedService>();

        return services;
    }
}
