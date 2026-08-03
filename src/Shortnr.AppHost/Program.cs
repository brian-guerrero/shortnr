using Aspire.Hosting.ApplicationModel;

var builder = DistributedApplication.CreateBuilder(args);

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

builder.AddProject<Projects.Shortnr_Web>("shortnr-web")
    .WithEnvironment("Authentication__Oidc__Authority", ReferenceExpression.Create($"{dexEndpoint}/dex"))
    .WithEnvironment("Smtp__Host", mailpit.GetEndpoint("smtp").Property(EndpointProperty.Host))
    .WithEnvironment("Smtp__Port", mailpit.GetEndpoint("smtp").Property(EndpointProperty.Port))
    .WaitFor(dex)
    .WaitFor(mailpit);

builder.Build().Run();
