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

### Basic

```bash
docker run -d \
  --name shortnr \
  -p 8080:8080 \
  -v shortnr-data:/data \
  --restart unless-stopped \
  ghcr.io/brian-guerrero/shortnr:latest
```

The SQLite database is stored at `/data/shortnr.db` inside the container. The named volume `shortnr-data` persists across restarts.

### Environment variables

```bash
docker run -d \
  --name shortnr \
  -p 8080:8080 \
  -v shortnr-data:/data \
  -e ConnectionStrings__DefaultConnection="Data Source=/data/shortnr.db" \
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

When behind a proxy, set `RateLimiting__TrustForwardedFor=true` so shortnr resolves the client IP from `X-Forwarded-For` instead of the proxy's address.

## Production checklist

- [ ] **Persistent volume** &mdash; mount a named volume or bind mount for `/data` so the SQLite database survives container restarts.
- [ ] **TLS** &mdash; place shortnr behind a reverse proxy with HTTPS.
- [ ] **Authentication** &mdash; enable OIDC if multiple users will access the instance.
- [ ] **Backups** &mdash; back up the SQLite file (`/data/shortnr.db`). It's a single file &mdash; `cp` or `rsync` is sufficient.
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

shortnr uses SQLite, which handles concurrent reads well but serializes writes. For most self-hosted deployments this is more than sufficient. If you outgrow SQLite:

- The `DbContext` is provider-agnostic. Switching to PostgreSQL requires changing the connection string and replacing `UseSqlite()` with `UseNpgsql()`.
- Click tracking uses an in-memory `Channel<ClickRecord>` + `ClickBatchProcessor` background service, so writes are batched and non-blocking.
