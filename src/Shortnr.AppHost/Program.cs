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
        .WithEnvironment("ConnectionStrings__DefaultConnection", shortnrDb)
        .WaitFor(postgres);
}

var docsDir = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", "docs"));
if (Directory.Exists(docsDir))
{
    builder.AddViteApp("shortnr-docs", "../../docs")
        .WithEnvironment("BROWSER", "none");
}

builder.Build().Run();
