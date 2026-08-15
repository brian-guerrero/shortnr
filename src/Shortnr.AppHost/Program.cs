using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

// Resolve the raw config value ourselves and feed it into AddParameter as the default,
// rather than the other way around. AddParameter("db-provider", "Sqlite")'s own resolved
// .Value does not reliably reflect a Parameters:db-provider override the same way a plain
// builder.Configuration[...] read does -- that mismatch left the "db-provider" parameter
// resource (and the Database__Provider env var wired from it) stuck on "Sqlite" even when
// this same config read below correctly saw "Postgres" and provisioned the Postgres
// resources, so shortnr-web got a Postgres connection string handed to the Sqlite driver.
// Computing it once here keeps both consistent.
var dbProviderValue = builder.Configuration["Parameters:db-provider"] ?? "Sqlite";
var dbProvider = builder.AddParameter("db-provider", dbProviderValue);

// Set by PostgresAppHostFixture (DistributedApplicationTestingBuilder) so the Postgres
// parity suite provisions just the database resource without also pulling/starting dex and
// mailpit, and gets its own isolated Postgres container/volume instead of sharing the
// persistent local dev database -- see the "postgres-test" naming below. Default is false
// so `dotnet run`/`aspire run` for real dev work is unchanged.
var isTestRun = builder.Configuration.GetValue("IsTestRun", false);

var shortnrWeb = builder.AddProject<Projects.Shortnr_Web>("shortnr-web")
    .WithEnvironment("Database__Provider", dbProvider);

// Mirrors the db-provider parameter above: read once, forward unconditionally into shortnr-web
// so its own AiInsights:Llm:Provider config always reflects what the AppHost resolved (same
// "AppHost wins under Aspire, appsettings.json wins standalone" shape as Database:Provider).
var llmProviderValue = builder.Configuration["Parameters:llm-provider"] ?? "OpenAI";
var llmProvider = builder.AddParameter("llm-provider", llmProviderValue);

// Toggle parameters for the two independent feature gates PRD-006/PRD-023 read from config
// (AiInsights:Enabled for the deterministic background pass, AiInsights:Llm:Enabled for the
// /insights "Ask AI" section) -- so both can be flipped the same way as db-provider/llm-provider
// (--Parameters:ai-insights=false / --Parameters:llm-enabled=false) instead of hand-editing
// appsettings.json. Defaults here intentionally differ from the committed appsettings.json
// defaults: under the AppHost the goal is a zero-friction "just try it" loop, so both default
// on -- an enabled-but-unconfigured LLM layer is inert (LlmInsightService's NotConfigured gate
// short-circuits before any network call), so there's no real cost to defaulting llm-enabled on.
var aiInsightsEnabledValue = builder.Configuration["Parameters:ai-insights"] ?? "true";
var aiInsightsEnabled = builder.AddParameter("ai-insights", aiInsightsEnabledValue);

var llmEnabledValue = builder.Configuration["Parameters:llm-enabled"] ?? "true";
var llmEnabled = builder.AddParameter("llm-enabled", llmEnabledValue);

shortnrWeb
    .WithEnvironment("AiInsights__Enabled", aiInsightsEnabled)
    .WithEnvironment("AiInsights__Llm__Enabled", llmEnabled)
    .WithEnvironment("AiInsights__Llm__Provider", llmProvider);

if (!isTestRun)
{
    var dex = builder.AddContainer("dex", "dexidp/dex", "v2.39.1")
        .WithBindMount("../../dex/config.yaml", "/etc/dex/config.yaml", isReadOnly: true)
        .WithArgs("dex", "serve", "/etc/dex/config.yaml")
        .WithHttpEndpoint(port: 5556, targetPort: 5556, name: "http")
        .WithLifetime(ContainerLifetime.Persistent);

    var dexEndpoint = dex.GetEndpoint("http");

    var mailpit = builder.AddContainer("mailpit", "axllent/mailpit", "latest")
        .WithHttpEndpoint(port: 8025, targetPort: 8025, name: "web-ui")
        .WithEndpoint(targetPort: 1025, name: "smtp")
        .WithLifetime(ContainerLifetime.Persistent);

    shortnrWeb
        .WithEnvironment("Authentication__Oidc__Authority", ReferenceExpression.Create($"{dexEndpoint}/dex"))
        .WithEnvironment("Smtp__Host", mailpit.GetEndpoint("smtp").Property(EndpointProperty.Host))
        .WithEnvironment("Smtp__Port", mailpit.GetEndpoint("smtp").Property(EndpointProperty.Port))
        .WaitFor(dex)
        .WaitFor(mailpit);
}

if (dbProviderValue.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
{
    // A distinct resource name -- and therefore a distinct container + named data volume --
    // for test runs, so the Postgres parity suite never shares data with (or gets reset by)
    // the persistent local dev database used for interactive troubleshooting. Both are
    // ContainerLifetime.Persistent and use an explicit named volume so data survives
    // container recreation, not just container reuse.
    var postgresName = isTestRun ? "postgres-test" : "postgres";
    var postgres = builder.AddPostgres(postgresName)
        .WithDataVolume($"shortnr-{postgresName}-data")
        .WithLifetime(ContainerLifetime.Persistent);

    var shortnrDb = postgres.AddDatabase("shortnr-db");

    shortnrWeb
        .WithEnvironment("Database__ConnectionString", shortnrDb)
        .WaitFor(postgres);
}

// Local-dev-only: when llm-provider is "Ollama", spin up a real Ollama server container
// (community-maintained hosting integration -- there's no first-party Aspire.Hosting.Ollama
// package) and auto-pull a small default model, so `dotnet run --project src\Shortnr.AppHost`
// gives a one-command local LLM to manually test /insights against. Resource is named
// "llm-ollama" to match the connection-string key AiInsightsModule looks up in Shortnr.Web.
// No automated test depends on this -- integration tests stub IChatClient directly instead
// (see ShortnrWebAppFactory).
if (llmProviderValue.Equals("Ollama", StringComparison.OrdinalIgnoreCase) && !isTestRun)
{
    var ollamaModelName = builder.Configuration["Parameters:llm-model"] is { Length: > 0 } configuredModel
        ? configuredModel
        : "phi4-mini:3.8b";

    var ollama = builder.AddOllama("ollama")
        .WithDataVolume("shortnr-ollama-data")
        .WithLifetime(ContainerLifetime.Persistent);

    var ollamaModel = ollama.AddModel("llm-ollama", ollamaModelName);

    shortnrWeb
        // AiInsightsModule's DI-time IChatClient construction reads the "llm-ollama"
        // connection string (below) for the endpoint, but LlmInsightService's own
        // pre-flight gate and the prompt-building calls in InsightsModel both read
        // LlmOptions.Model directly -- that's config-bound (AiInsights:Llm:Model), not
        // derived from the connection string, so it has to be forwarded explicitly or
        // the page shows "No AI model is configured" even though the client is wired.
        .WithEnvironment("AiInsights__Llm__Model", ollamaModelName)
        .WithReference(ollamaModel)
        .WaitFor(ollamaModel);
}

var docsDir = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", "docs"));
if (Directory.Exists(docsDir))
{
    builder.AddViteApp("shortnr-docs", "../../docs")
        .WithEnvironment("BROWSER", "none");
}

builder.Build().Run();
