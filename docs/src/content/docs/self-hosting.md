---
title: Self-hosting
description: Deploy shortnr in production with Docker, configure a reverse proxy, and manage persistence.
order: 2
---

# Self-hosting

shortnr is designed to run on minimal infrastructure: a single container, a single SQLite file, no external dependencies.

## Docker deployment

A prebuilt image is published to the GitHub Container Registry on every push to `main` (tagged `latest`) and on version releases (`vX.Y.Z`). Pin to a specific version for production deployments:

```bash
docker pull ghcr.io/brian-guerrero/shortnr:vX.Y.Z
```

### Basic (SQLite)

```bash
docker run -d \
  --name shortnr \
  -p 8080:8080 \
  -v shortnr-data:/data \
  --restart unless-stopped \
  ghcr.io/brian-guerrero/shortnr:latest
```

The SQLite database is stored at `/data/shortnr.db` inside the container. The named volume `shortnr-data` persists across restarts.

Authentication is **enabled by default** in the container. For personal/single-user use without an identity provider, add `-e Authentication__Enabled=false` to the run command above. For production, set `Authentication__Enabled=true` (already the default) along with your OIDC authority and credentials &mdash; see [Configuration](/shortnr/docs/configuration/) and [Running with authentication](/shortnr/docs/getting-started/#running-with-authentication).

### PostgreSQL

For production deployments with higher concurrency needs, use PostgreSQL:

```bash
docker compose -f docker-compose.postgres.yml up -d
```

Or manually:

```bash
docker run -d \
  --name shortnr \
  -p 8080:8080 \
  -e Database__Provider=Postgres \
  -e Database__ConnectionString="Host=postgres;Database=shortnr;Username=shortnr;Password=secret" \
  --restart unless-stopped \
  ghcr.io/brian-guerrero/shortnr:latest
```

### Environment variables

```bash
docker run -d \
  --name shortnr \
  -p 8080:8080 \
  -v shortnr-data:/data \
  -e Database__ConnectionString="Data Source=/data/shortnr.db" \
  -e Authentication__Enabled=true \
  -e Authentication__Oidc__Authority=https://your-idp.example.com \
  -e Authentication__Oidc__ClientId=shortnr-web \
  -e Authentication__Oidc__ClientSecret=your-secret \
  --restart unless-stopped \
  ghcr.io/brian-guerrero/shortnr:latest
```

See the [configuration reference](/shortnr/docs/configuration/) for all available settings.

## Reverse proxy

shortnr listens on HTTP by default. Place it behind a reverse proxy (Caddy, nginx, Traefik) for TLS termination.

### Caddy

```
shortnr.example.com {
  reverse_proxy localhost:8080
}
```

### nginx

```nginx
server {
    listen 443 ssl http2;
    server_name shortnr.example.com;

    ssl_certificate /etc/letsencrypt/live/shortnr.example.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/shortnr.example.com/privkey.pem;

    location / {
        proxy_pass http://localhost:8080;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

When behind a proxy, set `RateLimiting__TrustForwardedFor=true` so shortnr resolves the client IP from `X-Forwarded-For` instead of the proxy's address, and `Hosting__TrustForwardedHeaders=true` so it sees the real `https` scheme (needed for correct OIDC callback URLs — without it, the OIDC handler builds `redirect_uri` as `http://...` and your IdP will reject it as an unregistered callback). Only set either flag when a proxy you control is guaranteed to overwrite these headers on every request, never on a directly internet-exposed instance.

## Production checklist

### SQLite

- [ ] **Persistent volume** &mdash; mount a named volume or bind mount for `/data` so the SQLite database survives container restarts.
- [ ] **Backups** &mdash; back up the SQLite file (`/data/shortnr.db`). It's a single file &mdash; `cp` or `rsync` is sufficient.

### PostgreSQL

- [ ] **Database server** &mdash; run Postgres in a separate container or as a managed service.
- [ ] **Backups** &mdash; configure database-level backups (`pg_dump` or managed snapshots).
- [ ] **Connection security** &mdash; use strong passwords and consider SSL/TLS for database connections.

### All deployments

- [ ] **TLS** &mdash; place shortnr behind a reverse proxy with HTTPS.
- [ ] **Authentication** &mdash; auth is enabled by default; configure `Authentication__Oidc__Authority`, `ClientId`, and `ClientSecret` for your OIDC provider if you need multi-user access, or set `Authentication__Enabled=false` for single-user use.
- [ ] **Rate limiting** &mdash; the built-in rate limits are sensible defaults. For very high redirect volume, add proxy/CDN-level limiting.
- [ ] **GeoIP** (optional) &mdash; configure `GeoIp__MaxMindAccountId` and `GeoIp__MaxMindLicenseKey` to enrich clicks with country/city data.

## Local development with Aspire

For local development with a full OIDC setup:

```bash
dotnet run --project src/Shortnr.AppHost
```

This starts `Shortnr.Web` and a local [Dex](https://dexidp.io) container (a spec-compliant OpenID Connect test IdP) together, wired to the same app graph. Requires a running container runtime (Docker Desktop / Podman).

Two test users are provisioned: `test@example.com` and `test2@example.com` (both password `password`).

## Scaling considerations

shortnr supports two database providers:

- **SQLite** &mdash; Handles concurrent reads well but serializes writes. Ideal for single-instance, low-to-moderate traffic deployments.
- **PostgreSQL** &mdash; MVCC concurrency, and shareable across replicas. Required to run more than one instance behind a load balancer, since a SQLite file can't be shared.

Switch providers via environment variables (`Database__Provider` and `Database__ConnectionString`). See the [database guide](/shortnr/docs/configuration/database/) for the full comparison, and the [migration guide](/shortnr/docs/database-migration/) for moving existing data.

Click tracking uses an in-memory `Channel<ClickRecord>` + `ClickBatchProcessor` background service, so writes are batched and non-blocking regardless of database provider.
