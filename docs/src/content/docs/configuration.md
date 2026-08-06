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
| `ConnectionStrings__DefaultConnection` | `Data Source=shortnr.db` | SQLite connection string. Override via environment variable. |
| `ASPNETCORE_URLS` | `http://+:5000` (dev) / `http://+:8080` (Docker) | Listening address. |

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

Override the database location:

```bash
dotnet run --project src/Shortnr.Web -- \
  --ConnectionStrings:DefaultConnection="Data Source=/mnt/data/shortnr.db"
```

Docker:

```bash
docker run -e ConnectionStrings__DefaultConnection="Data Source=/data/shortnr.db" ghcr.io/brian-guerrero/shortnr:latest
```
