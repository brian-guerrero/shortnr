---
title: Configuration
description: Complete reference for all shortnr configuration settings, environment variables, and appsettings.json options.
order: 3
---

# Configuration

shortnr is configured via `appsettings.json`, environment variables, or command-line arguments. Environment variables use `__` as the hierarchy separator (e.g., `Authentication__Enabled`).

## Core settings

| Setting | Default | Description |
|---------|---------|-------------|
| `Database__Provider` | `Sqlite` | Database provider: `Sqlite` or `Postgres`. |
| `Database__ConnectionString` | *(empty)* | Connection string for the selected provider. Falls back to `ConnectionStrings__DefaultConnection` if not set. |
| `ConnectionStrings__DefaultConnection` | `Data Source=shortnr.db` | Legacy connection string setting. Used as fallback for SQLite. |
| `ASPNETCORE_URLS` | `http://+:5000` (dev) / `http://+:8080` (Docker) | Listening address. |

### Database providers

shortnr supports two database providers:

- **SQLite** (default) &mdash; Zero-config, file-based. Ideal for single-instance deployments and development.
- **PostgreSQL** &mdash; MVCC concurrency, shared across replicas. Required if you run more than one instance.

MySQL/MariaDB is **not currently supported** &mdash; setting `Database__Provider=MySql` fails at startup.

Switch providers via environment variables:

```bash
Database__Provider=Postgres \
Database__ConnectionString="Host=localhost;Database=shortnr;Username=shortnr;Password=secret" \
dotnet run --project src/Shortnr.Web
```

See the [database guide](/shortnr/docs/configuration/database/) for the full comparison, and the [migration guide](/shortnr/docs/database-migration/) for moving existing data from SQLite to Postgres.

## Authentication

| Setting | Default | Description |
|---------|---------|-------------|
| `Authentication__Enabled` | `true` | Set to `false` to disable OIDC entirely &mdash; no login UI, no access control, dashboard shows all data. |
| `Authentication__Oidc__Authority` | `http://localhost:5556/dex` | OpenID Connect issuer URL. Set automatically by `Shortnr.AppHost` when running under Aspire. |
| `Authentication__Oidc__ClientId` | `shortnr-web` | Must match `staticClients` in `dex/config.yaml` or your IdP configuration. |
| `Authentication__Oidc__ClientSecret` | *(dev-only value)* | Must match your IdP configuration. |

### Disabling authentication

```bash
dotnet run --project src/Shortnr.Web -- Authentication:Enabled=false
```

Or in `appsettings.Development.json`:

```json
{ "Authentication": { "Enabled": false } }
```

When disabled: `/account/login` and `/account/logout` return 404, the login link and user menu are hidden from the nav, the dashboard is accessible without signing in, and all data is shown unfiltered.

## OAuth (MCP clients)

| Setting | Default | Description |
|---------|---------|-------------|
| `OAuth__Issuer` | `http://localhost:5156` | Public base URL of this deployment; must be the real `https://` URL in production. |
| `OAuth__Resource` | `http://localhost:5156/mcp` | The MCP resource URI (RFC 8707 audience). |
| `OAuth__AccessTokenLifetimeMinutes` | `60` | Access token lifetime. |
| `OAuth__RefreshTokenLifetimeDays` | `14` | Refresh token lifetime. |
| `OAuth__SigningCertificate` / `OAuth__SigningCertificatePassword` | *(dev certs auto-generated)* | Base64 PKCS#12 cert (Digital Signature key usage) used to sign OAuth tokens. Required outside `Development`. |
| `OAuth__EncryptionCertificate` / `OAuth__EncryptionCertificatePassword` | *(dev certs auto-generated)* | Base64 PKCS#12 cert (Key Encipherment key usage) used to encrypt OAuth tokens. Required outside `Development`. |

See the [MCP server docs](/shortnr/docs/mcp/#deploying-the-oauth-server) for certificate generation steps and common deployment pitfalls.

## GeoIP / MaxMind

| Setting | Default | Description |
|---------|---------|-------------|
| `GeoIp__MaxMindAccountId` | *(empty)* | MaxMind account ID. GeoIP enrichment is disabled until both account ID and license key are set. |
| `GeoIp__MaxMindLicenseKey` | *(empty)* | MaxMind license key. Enables downloading GeoLite2-City from MaxMind's official endpoint on startup + Wed/Sat 12:00 UTC. |
| `GeoIp__DatabasePath` | `wwwroot/data/GeoLite2-City.mmdb` | Where the downloaded database is stored. |

When configured, shortnr downloads the GeoLite2 City database from MaxMind and uses it to enrich click events with country/city data. Both keys come from a [MaxMind account](https://www.maxmind.com).

- Without a license key, enrichment is a no-op: no download is attempted and clicks carry no geo data.
- The database is never bundled with the repo; it is downloaded at runtime.
- Per the GeoLite2 EULA, the running app displays the required attribution in its footer.

## Hosting

| Setting | Default | Description |
|---------|---------|-------------|
| `Hosting__TrustForwardedHeaders` | `false` | When `true`, trust `X-Forwarded-For`/`X-Forwarded-Proto` for the request scheme/host (needed so the OIDC handler builds `https://` callback URLs when TLS is terminated at a reverse proxy). Only enable this when a proxy you control is guaranteed to overwrite these headers on every request — otherwise a client can spoof `X-Forwarded-Proto: https` to bypass HTTPS-only checks. |

## Rate limiting

| Setting | Default | Description |
|---------|---------|-------------|
| `RateLimiting__TrustForwardedFor` | `false` | When `true`, resolve the client IP from the left-most `X-Forwarded-For` hop (for deployments behind a reverse proxy). |
| `RateLimiting__Shorten__PerMinute` | `10` | Per-IP request cap per minute for the shorten form (`POST /`). |
| `RateLimiting__Shorten__PerDay` | `200` | Per-IP daily cap for the shorten form. |
| `RateLimiting__Redirect__PerMinute` | `300` | Per-IP request cap per minute for the redirect endpoint (`GET /{shortCode}`). |
| `RateLimiting__Redirect__PerDay` | `10000` | Per-IP daily cap for the redirect endpoint. |

The redirect limits are deliberately generous so legitimate traffic (including viral spikes) is never throttled. For very high redirect volume, configure additional limiting at the reverse proxy or CDN edge.

### API rate limiting

The `/api/v1` endpoints use a separate chained rate limiter per API key: 60 requests/minute burst + 1000/day cap. Over-limit requests receive `429 Too Many Requests`.

## Connection string examples

### SQLite (default)

```bash
dotnet run --project src/Shortnr.Web -- \
  --ConnectionStrings:DefaultConnection="Data Source=/mnt/data/shortnr.db"
```

Docker:

```bash
docker run -e ConnectionStrings__DefaultConnection="Data Source=/data/shortnr.db" ghcr.io/brian-guerrero/shortnr:latest
```

### PostgreSQL

```bash
docker run \
  -e Database__Provider=Postgres \
  -e Database__ConnectionString="Host=postgres;Database=shortnr;Username=shortnr;Password=secret" \
  ghcr.io/brian-guerrero/shortnr:latest
```

Or use the provided Compose file, which brings up shortnr and Postgres together:

```bash
docker compose -f docker-compose.postgres.yml up -d
```
