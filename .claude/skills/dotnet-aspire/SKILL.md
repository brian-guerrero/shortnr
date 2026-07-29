---
name: dotnet-aspire
description: Guided implementation for adding .NET Aspire orchestration to the shortnr solution — AppHost/ServiceDefaults project layout, wiring Shortnr.Web as a project resource, and adding container resources (e.g. Dex) on the same app-graph network. Use this whenever adding, changing, or debugging anything under src/Shortnr.AppHost or src/Shortnr.ServiceDefaults, or when wiring a new container resource into the AppHost.
---

# .NET Aspire in shortnr

Reference notes for this repo specifically — not a general Aspire tutorial. Read this
before touching `src/Shortnr.AppHost` or `src/Shortnr.ServiceDefaults`.

## Why Aspire here

Aspire is the *local orchestrator* for shortnr: one `dotnet run` starts `Shortnr.Web`
plus any supporting containers (Dex, and later Postgres/Redis if we swap providers) as a
single app graph, with service discovery wiring the URLs between them automatically. It
does **not** replace the Dockerfile — that's still used for the standalone production
image. Aspire is dev/test orchestration only.

## Project layout

Two projects get added to `Shortnr.slnx`, both under `src/`:

- **`Shortnr.AppHost`** — the orchestrator entry point (`dotnet run --project src/Shortnr.AppHost`
  starts everything). References `Shortnr.Web` as a project resource. Has `IsAspireHost`
  set via the `Aspire.AppHost.Sdk` MSBuild SDK, not the plain web/library SDK.
- **`Shortnr.ServiceDefaults`** — a shared class library with `AddServiceDefaults()` /
  `MapDefaultEndpoints()` extension methods (OpenTelemetry, health checks, service
  discovery, resilience handlers). `Shortnr.Web` references this and calls both.

`Shortnr.AppHost` is the only project that references `Shortnr.Web` directly (via
`<ProjectReference>` generated as `Projects.Shortnr_Web` in the AppHost's `Program.cs`).
`Shortnr.Web` never references `Shortnr.AppHost`.

## Package versions

Pin AppHost/Hosting packages to the same major.minor as the SDK version pulled in via
`Aspire.AppHost.Sdk` — mismatches between the SDK and `Aspire.Hosting.AppHost` throw at
build time ("Newer version of Aspire.Hosting.AppHost required"). As of this writing the
current stable line is **13.x** (targets .NET 8+, so it's fine on the repo's .NET 10
SDK). Check installed/available version before bumping:

```bash
dotnet nuget list source                       # sanity check feeds
dotnet add src/Shortnr.AppHost package Aspire.Hosting.AppHost --version 13.*
```

`Shortnr.AppHost.csproj` needs the Aspire App Host SDK at the top:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <Sdk Name="Aspire.AppHost.Sdk" Version="13.4.6" />
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <IsAspireHost>true</IsAspireHost>
    <UserSecretsId>...</UserSecretsId>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Aspire.Hosting.AppHost" Version="13.4.6" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Shortnr.Web\Shortnr.Web.csproj" />
  </ItemGroup>
</Project>
```

## Core AppHost APIs used in this repo

```csharp
var builder = DistributedApplication.CreateBuilder(args);

// A project resource — runs as a normal `dotnet run` process locally, not a container.
var web = builder.AddProject<Projects.Shortnr_Web>("shortnr-web");

// A container resource — pulled/run via the local container runtime (Docker/Podman).
var dex = builder.AddContainer("dex", "dexidp/dex", "v2.39.1")
    .WithBindMount("../../dex/config.yaml", "/etc/dex/config.yaml", isReadOnly: true)
    .WithArgs("serve", "/etc/dex/config.yaml")               // dex's ENTRYPOINT is the binary; don't repeat "dex"
    .WithHttpEndpoint(port: 5556, targetPort: 5556, name: "http")
    .WithLifetime(ContainerLifetime.Persistent);              // survive AppHost restarts during dev

var dexEndpoint = dex.GetEndpoint("http");

web.WithReference(dex)                                        // injects services__dex__http__0 discovery env var
   .WithEnvironment("Authentication__Oidc__Authority", dexEndpoint)
   .WaitFor(dex);                                              // don't start web until dex is accepting connections

builder.Build().Run();
```

Key points:

- **`AddContainer(name, image, tag)`** creates the container resource. Prefer a pinned
  tag over `latest` so local runs are reproducible.
- **`WithBindMount(source, target, isReadOnly)`** — `source` is relative to the AppHost
  project directory, not the repo root or cwd.
- **`WithHttpEndpoint(port:, targetPort:, name:)`** — pass a *fixed* host `port` (not
  auto-allocated) for anything a browser redirects to directly (like an OIDC authorize
  endpoint), because the redirect URL is baked into client config/tokens and can't be
  re-resolved after allocation the way service-to-service calls can.
- **`GetEndpoint(name)`** returns an `EndpointReference` — has no concrete URL until the
  AppHost has actually allocated ports at startup. Pass it straight into
  `WithEnvironment(name, EndpointReference)`; don't call `.ToString()` on it during the
  `Program.cs` build phase, only in an env-var callback (which Aspire runs after
  allocation).
- **`WithReference(resource)`** wires Aspire service discovery: the consuming resource
  gets `services__<resourceName>__<endpointName>__0` env vars it can resolve via
  `Microsoft.Extensions.ServiceDiscovery` (already included by `AddServiceDefaults()`).
  Explicit `WithEnvironment(name, endpoint)` calls (as above) are for env var names our
  own code expects (e.g. ASP.NET Core config keys like `Authentication__Oidc__Authority`)
  that don't match the discovery convention.
- **`WaitFor(dependency)`** blocks a resource's startup until the dependency reports
  healthy — use it so `Shortnr.Web` doesn't start racing Dex's discovery document.
- **Same network**: for **container** resources, Aspire places them all on a shared
  Docker/Podman network per AppHost run automatically — no explicit network wiring
  needed. `Shortnr.Web` itself runs as a host process locally (not containerized) under
  `dotnet run --project src/Shortnr.AppHost`, so it reaches Dex over the published host
  port from `WithHttpEndpoint`, same as a browser does — which is what makes the
  browser-facing authorize URL and the backend-facing token/discovery URL trivially
  consistent (`http://localhost:5556/dex` from both sides) during local dev.

## Dev workflow

- **Run everything**: `dotnet run --project src/Shortnr.AppHost` — opens the Aspire
  dashboard (URL printed to console) showing resource health, logs, traces.
- **Requires** a running container runtime (Docker Desktop / Podman) for the Dex
  resource even though `Shortnr.Web` itself doesn't need containerization.
- **`Shortnr.Web` still runs standalone** (`dotnet run --project src/Shortnr.Web`)
  without Aspire — in that mode Dex isn't started automatically, so either start the Dex
  container manually (see the `dex-oidc` skill) or point `Authentication:Oidc:Authority`
  at nothing and expect auth to be unavailable. Aspire is additive, not a hard
  dependency for running the web app.

## Common pitfalls

- Forgetting `WaitFor` → Dex's OIDC discovery document (`/.well-known/openid-configuration`)
  isn't ready yet when `Shortnr.Web` starts and calls `AddOpenIdConnect`'s metadata
  fetch — causes a startup-time or first-request failure. Prefer configuring the OIDC
  handler to fetch metadata lazily/on first challenge rather than eagerly at startup if
  Dex might not be up yet regardless.
- SDK/package version mismatch (`Aspire.AppHost.Sdk` vs `Aspire.Hosting.AppHost`) — keep
  both at the same version.
- Bind mount paths are resolved relative to the **AppHost csproj directory**, a common
  source of "file not found in container" errors.
- Don't containerize `Shortnr.Web` under Aspire unless you also need the Dockerfile-based
  image locally — the existing `Dockerfile` at repo root remains the source of truth for
  the production container image; Aspire's project-resource mode is for fast local dev.
