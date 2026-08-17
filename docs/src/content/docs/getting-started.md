---
title: Getting started
description: Get shortnr running in under five minutes with Docker or from source.
order: 1
---

# Getting started

shortnr runs from a single Docker command or directly from source with the .NET 10 SDK. This guide covers both paths.

## Docker (recommended)

A prebuilt image is published to the GitHub Container Registry on every push to `main` (tagged `latest`) and on version releases (`vX.Y.Z`).

```bash
docker run -p 8080:8080 -v shortnr-data:/data ghcr.io/brian-guerrero/shortnr:latest
```

Open `http://localhost:8080`. The SQLite database is stored in the `shortnr-data` named Docker volume and persists across container restarts.

By default, authentication is disabled &mdash; every link and click is visible to whoever can reach the instance. This is fine for personal or single-user use.

Prefer Postgres, or planning to run more than one instance? Bring both up together:

```bash
docker compose -f docker-compose.postgres.yml up -d
```

See [Database](/shortnr/docs/configuration/database/) for when that's worth doing.

To build the image from source instead:

```bash
git clone https://github.com/brian-guerrero/shortnr.git
cd shortnr
docker build -t shortnr .
docker run -p 8080:8080 -v shortnr-data:/data shortnr
```

## From source

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Docker (optional, for containerised deployment or running under Aspire)

### Steps

```bash
git clone https://github.com/brian-guerrero/shortnr.git
cd shortnr
dotnet build
dotnet run --project src/Shortnr.Web/Shortnr.Web.csproj
```

Open `http://localhost:5156`.

> `dotnet build` triggers `Microsoft.Web.LibraryManager.Build`, which downloads htmx, Chart.js, and Alpine.js into `wwwroot/lib/` automatically. No manual `libman restore` needed.

## Choosing a database

shortnr runs on **SQLite** by default &mdash; a single file, no setup, and enough for most self-hosted instances. Nothing to configure to get started.

It also supports **PostgreSQL**, which you'll want once you need to run more than one instance behind a load balancer (a SQLite file can't be shared between replicas):

```bash
Database__Provider=Postgres \
Database__ConnectionString="Host=localhost;Database=shortnr;Username=shortnr;Password=secret" \
dotnet run --project src/Shortnr.Web
```

The schema is created automatically on first start under either provider. See the [database guide](/shortnr/docs/configuration/database/) for the full comparison and migration steps.

## What you can do next

- **Shorten your first link** &mdash; paste any URL into the input field on the home page.
- **View the dashboard** at `/dashboard` &mdash; see live metrics, click counts, and charts.
- **Generate a QR code** &mdash; click "Show QR" next to any short link.
- **Create a bio page** at `/bio/edit` &mdash; build a Linktree-style page backed by your short links.

## Running with authentication

To enable OIDC authentication (works with Dex, Authentik, Okta, Auth0, Azure AD):

```bash
dotnet run --project src/Shortnr.Web -- \
  Authentication:Enabled=true \
  Authentication:Oidc:Authority=http://your-idp/.well-known/openid-configuration \
  Authentication:Oidc:ClientId=shortnr-web \
  Authentication:Oidc:ClientSecret=your-secret
```

Or start under Aspire with a local Dex instance:

```bash
dotnet run --project src/Shortnr.AppHost
```

This starts `Shortnr.Web` and a local [Dex](https://dexidp.io) container together, wired to the same app graph. Requires a running container runtime (Docker Desktop / Podman).

## Next steps

- [Self-hosting guide](/shortnr/docs/self-hosting/) &mdash; production deployment, reverse proxy, and persistence
- [Configuration reference](/shortnr/docs/configuration/) &mdash; all available settings
- [Database](/shortnr/docs/configuration/database/) &mdash; SQLite vs Postgres, and how to migrate
- [Architecture overview](/shortnr/docs/architecture/) &mdash; how shortnr works under the hood
