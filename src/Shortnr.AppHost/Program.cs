using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

var dbProvider = builder.AddParameter("db-provider", "Sqlite");

// Lets integration tests (DistributedApplicationTestingBuilder) provision just the database
// resource without also pulling/starting dex and mailpit, which the Postgres parity suite
// never touches. Default is false so `dotnet run`/`aspire run` for real dev work is unchanged.
var skipAuxServices = builder.Configuration.GetValue("SkipAuxServices", false);

var shortnrWeb = builder.AddProject<Projects.Shortnr_Web>("shortnr-web")
    .WithEnvironment("Database__Provider", dbProvider);

if (!skipAuxServices)
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

var dbProviderValue = builder.Configuration["Parameters:db-provider"] ?? "Sqlite";

if (dbProviderValue.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
{
    var postgres = builder.AddPostgres("postgres")
        .WithLifetime(ContainerLifetime.Persistent);

    var shortnrDb = postgres.AddDatabase("shortnr-db");

    shortnrWeb
        .WithEnvironment("Database__ConnectionString", shortnrDb)
        .WaitFor(postgres);
}
else if (dbProviderValue.Equals("MySql", StringComparison.OrdinalIgnoreCase))
{
    var mysql = builder.AddMySql("mysql")
        .WithLifetime(ContainerLifetime.Persistent);

    var shortnrDb = mysql.AddDatabase("shortnr-db");

    shortnrWeb
        .WithEnvironment("Database__ConnectionString", shortnrDb)
        .WaitFor(mysql);
}

var docsDir = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", "docs"));
if (Directory.Exists(docsDir))
{
    builder.AddViteApp("shortnr-docs", "../../docs")
        .WithEnvironment("BROWSER", "none");
}

builder.Build().Run();
