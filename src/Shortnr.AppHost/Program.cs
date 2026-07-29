using Aspire.Hosting.ApplicationModel;

var builder = DistributedApplication.CreateBuilder(args);

// Dex is a local, spec-compliant OpenID Connect test IdP — see the `dex-oidc` skill for
// config.yaml shape. Fixed host port so both the browser and Shortnr.Web reach it at the
// same URL during local dev (no split issuer/internal-URL problem to work around).
var dex = builder.AddContainer("dex", "dexidp/dex", "v2.39.1")
    .WithBindMount("../../dex/config.yaml", "/etc/dex/config.yaml", isReadOnly: true)
    .WithArgs("serve", "/etc/dex/config.yaml")
    .WithHttpEndpoint(port: 5556, targetPort: 5556, name: "http")
    .WithLifetime(ContainerLifetime.Persistent);

var dexEndpoint = dex.GetEndpoint("http");

builder.AddProject<Projects.Shortnr_Web>("shortnr-web")
    .WithEnvironment("Authentication__Oidc__Authority", ReferenceExpression.Create($"{dexEndpoint}/dex"))
    .WaitFor(dex);

builder.Build().Run();
