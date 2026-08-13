# shortnr

A URL shortener with a real-time dashboard. Built with ASP.NET Core Razor Pages, HTMX, and EF Core on SQLite or Postgres.

## Features

- Shorten any URL to a 6-character code
- Duplicate URL detection — shortening the same URL returns the existing code
- Click tracking with IP, user agent, referrer, and geographic enrichment (async batch processing)
- Real-time dashboard with metrics, sortable link/click tables, and Chart.js charts
- QR code generation — inline on index page, shareable page at `/qr/{shortCode}`, downloadable PNG at `/api/qr/{shortCode}`
- Link-in-bio pages — personal bio page with theme picker, link reordering, and avatar support
- Optional OIDC authentication via Dex (or any OIDC provider); disable entirely with one config flag
- Per-user dashboard — when auth is enabled, dashboard and `/api/metrics` show only the signed-in user's links
- Team workspaces — create workspaces, invite members by email, assign roles (Owner/Editor/Viewer), and switch between workspaces to scope dashboards and link creation
- Branded domains — add and verify custom domains for vanity links
- REST API v1 — create, list, update, and delete links with API keys at `/api/v1`
- CLI (`shortnr-cli`) — manage links from the command line, wraps the `/api/v1` API
- MCP server — AI agents can manage links and bio pages via the Model Context Protocol
- User menu with Gravatar avatar, workspace switcher, and sign-out dropdown
- Bring your own database — SQLite by default (zero-config), Postgres for scale, selected with one config setting
- Docker-ready with a persistent SQLite volume; prebuilt image on ghcr.io

## Project structure

```
shortnr/
├── src/
│   ├── Shortnr.Data/          # EF Core entities, AppDbContext, SQLite migrations,
│   │                          #   DatabaseProviderHelper (provider selection)
│   ├── Shortnr.Data.Postgres/ # Postgres-flavored EF Core migrations (separate history)
│   ├── Shortnr.Web/           # Razor Pages app
│   │   ├── Features/          # Feature modules, each with an Add<Feature>Feature() extension:
│   │   │                      #   ShortLinks, ClickTracking, Authentication, Domains,
│   │   │                      #   Workspaces, BioPages, Api, Mcp, OAuth, Webhooks, Email,
│   │   │                      #   GeoIp, AiActivity, Insights, Infrastructure
│   │   ├── Pages/             # Index, Dashboard, QR, Bio, Settings pages + Shared partials
│   │   ├── wwwroot/           # Static files (lib/ is gitignored, restored by LibMan)
│   │   ├── libman.json        # Frontend dependency manifest
│   │   └── Program.cs         # App setup, feature-module wiring, minimal API endpoints
│   ├── Shortnr.Cli/           # CLI tool (shortnr-cli) wrapping the /api/v1 API
│   ├── Shortnr.AppHost/       # .NET Aspire orchestrator (web app + Dex, MailPit, Postgres)
│   └── Shortnr.ServiceDefaults/  # Shared health checks / OpenTelemetry / service discovery
├── tests/
│   ├── Shortnr.Tests.Unit/        # xUnit unit tests (service logic, EF InMemory)
│   └── Shortnr.Tests.Integration/ # xUnit integration tests (WebApplicationFactory +
│                                  #   TestAuthHandler, plus the Postgres parity suite)
├── docs/                      # Astro + Starlight documentation site
├── dex/
│   └── config.yaml            # Dex (test OIDC provider) config — see .claude/skills/dex-oidc
├── Dockerfile
├── .dockerignore
├── LICENSE                  # Business Source License 1.1 (→ Apache 2.0)
├── CONTRIBUTING.md
└── AGENTS.md
```

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Docker (optional, for containerised deployment or running under Aspire)

## Getting started

```bash
git clone <repo-url>
cd shortnr
dotnet build        # also restores frontend assets via LibMan automatically
dotnet run --project src/Shortnr.Web/Shortnr.Web.csproj
```

Open `http://localhost:5156`.

> `dotnet build` triggers `Microsoft.Web.LibraryManager.Build`, which downloads Pico CSS, htmx, Chart.js, and Alpine.js into `wwwroot/lib/` automatically. No manual `libman restore` needed.

Running this way starts the app **without authentication** — the dashboard is freely accessible and all data is shared. To run with full auth, either disable it explicitly (see below) or start under Aspire with a live Dex instance:

```bash
dotnet run --project src/Shortnr.AppHost
```

This starts `Shortnr.Web` **and** a local [Dex](https://dexidp.io) container (a
spec-compliant OpenID Connect test IdP) together, wired to the same app graph, and
prints the Aspire dashboard URL to the console. Requires a running container runtime
(Docker Desktop / Podman). See `.claude/skills/dotnet-aspire` and
`.claude/skills/dex-oidc` for how the orchestration and OIDC config fit together, and
`dex/config.yaml` for the test login credentials.

## Running tests

```bash
dotnet test
```

Runs both `Shortnr.Tests.Unit` (fast, in-process) and `Shortnr.Tests.Integration` (full web host, isolated SQLite DB per test class).

The integration project also includes a Postgres parity suite that runs against a real
Postgres container. It needs Docker and is skipped — not failed — without it, so a bare
`dotnet test` passes either way. Run it on its own with:

```bash
dotnet test --filter "Category=Postgres"
```

## Docker

A prebuilt image is published to the GitHub Container Registry on every push to `main` and on version tags (`vX.Y.Z`).

```bash
docker run -p 8080:8080 -v shortnr-data:/data ghcr.io/brian-guerrero/shortnr:latest
```

Open `http://localhost:8080`. The SQLite database is stored in the `shortnr-data` named volume at `/data/shortnr.db` and persists across container restarts.

To build from source instead:

```bash
docker build -t shortnr .
docker run -p 8080:8080 -v shortnr-data:/data shortnr
```

## Multi-database support

shortnr runs on either SQLite or Postgres, selected with `Database__Provider`:

| Provider | When to use it |
|----------|----------------|
| `Sqlite` *(default)* | Zero-config. No server to run — the database is a single file (`shortnr.db`), created and migrated on first start. Right for self-hosting, single-instance deployments, and local development. |
| `Postgres` | For scale. Concurrent writers, multiple app instances behind a load balancer, and managed-backup/replication setups. Point it at any Postgres server with `Database__ConnectionString`. |

```bash
# SQLite (default — nothing to set)
dotnet run --project src/Shortnr.Web

# Postgres
Database__Provider=Postgres \
Database__ConnectionString="Host=localhost;Port=5432;Database=shortnr;Username=postgres;Password=postgres" \
dotnet run --project src/Shortnr.Web
```

Either way, the schema is created and kept up to date automatically at startup via
`db.Database.Migrate()`. Each provider has its own migration history — SQLite's lives in
`Shortnr.Data`, Postgres's in `Shortnr.Data.Postgres` — because EF Core replays a
migration's recorded operations verbatim and cannot translate SQLite-flavored DDL into
Postgres-native DDL. Switching providers points the app at a different (empty) database;
there is no built-in data migration between the two.

For local Postgres development, `Shortnr.AppHost` provisions a Postgres container for you:

```bash
dotnet run --project src/Shortnr.AppHost -- --Parameters:db-provider=Postgres
```

It starts a Postgres container on a named volume and wires the connection string into the
web app automatically.

## Configuration

| Setting | Default | Description |
|---------|---------|-------------|
| `Database__Provider` | `Sqlite` | Database engine: `Sqlite` or `Postgres`. See [Multi-database support](#multi-database-support). |
| `Database__ConnectionString` | *(empty)* | Connection string for the selected provider. When empty, falls back to `ConnectionStrings__DefaultConnection`. Required for `Postgres`. |
| `ConnectionStrings__DefaultConnection` | `Data Source=shortnr.db` | Legacy connection string, used only when `Database__ConnectionString` is empty. |
| `ASPNETCORE_URLS` | `http://+:5000` (dev) / `http://+:8080` (Docker) | Listening address. |
| `Authentication__Enabled` | `true` | Set to `false` to disable OIDC entirely — no login UI, no access control, dashboard shows all data. |
| `Authentication__Oidc__Authority` | `http://localhost:5556/dex` | OpenID Connect issuer URL. Set automatically by `Shortnr.AppHost` when running under Aspire. |
| `Authentication__Oidc__ClientId` / `Authentication__Oidc__ClientSecret` | `shortnr-web` / dev-only value | Must match `staticClients` in `dex/config.yaml`. |
| `OAuth__Issuer` / `OAuth__Resource` | `http://localhost:5156` / `http://localhost:5156/mcp` | OAuth 2.1 issuer + MCP resource URI for AI clients. `Issuer` must be the real `https://` URL in production. |
| `OAuth__SigningCertificate` / `OAuth__EncryptionCertificate` | *(dev certs auto-generated)* | Base64 PKCS#12 certs required outside `Development` — see [MCP server docs](https://brian-guerrero.github.io/shortnr/docs/mcp/#deploying-the-oauth-server) for generation steps and key-usage requirements. |
| `GeoIp__MaxMindAccountId` | *(empty)* | MaxMind account ID. **GeoIP enrichment is disabled until both account ID and license key are set.** |
| `GeoIp__MaxMindLicenseKey` | *(empty)* | MaxMind license key. Enables downloading GeoLite2-City from MaxMind's official endpoint on startup + Wed/Sat 12:00 UTC. |
| `GeoIp__DatabasePath` | `wwwroot/data/GeoLite2-City.mmdb` | Where the downloaded database is stored. |
| `Hosting__TrustForwardedHeaders` | `false` | When `true`, trust `X-Forwarded-For`/`X-Forwarded-Proto` for the request scheme/host (needed for `https://` OIDC callback URLs behind a TLS-terminating proxy). Only enable behind a proxy you control — otherwise a client can spoof `X-Forwarded-Proto: https` to bypass HTTPS-only checks. |
| `RateLimiting__TrustForwardedFor` | `false` | When `true`, resolve the client IP from the left-most `X-Forwarded-For` hop (for deployments behind a reverse proxy). |
| `RateLimiting__Shorten__PerMinute` | `10` | Per-IP request cap per minute for the shorten form (`POST /`). Over-limit requests get `429`. |
| `RateLimiting__Shorten__PerDay` | `200` | Per-IP daily cap for the shorten form. |
| `RateLimiting__Redirect__PerMinute` | `300` | Per-IP request cap per minute for the redirect endpoint (`GET /{shortCode}`). Deliberately generous — add proxy/CDN limiting for very high redirect volume. |
| `RateLimiting__Redirect__PerDay` | `10000` | Per-IP daily cap for the redirect endpoint. |

### GeoIP / MaxMind attribution

When `GeoIp__MaxMindAccountId` and `GeoIp__MaxMindLicenseKey` are configured,
shortnr downloads the GeoLite2 City database from MaxMind's official,
license-keyed endpoint (`download.maxmind.com`) and uses it to enrich click
events with country/city data. Both keys come from a
[MaxMind account](https://www.maxmind.com) — downloading requires accepting the
[GeoLite2 EULA](https://www.maxmind.com/en/geolite2/eula).

- Without a license key, enrichment is a **no-op**: no download is attempted and
  clicks simply carry no geo data (fail open).
- The database is never bundled with the repo; it is downloaded at runtime and
  is not part of the distribution.
- Per the GeoLite2 EULA, the running app displays the required attribution in
  its footer:
  > This product includes GeoLite2 data created by MaxMind, available from
  > https://www.maxmind.com

### Disabling authentication

```bash
dotnet run --project src/Shortnr.Web -- Authentication:Enabled=false
```

Or in `appsettings.Development.json`:

```json
{ "Authentication": { "Enabled": false } }
```

When disabled: `/account/login` and `/account/logout` return 404, the login link and user menu are hidden from the nav, the dashboard is accessible without signing in, and all data is shown unfiltered.

Example connection string override:

```bash
dotnet run --project src/Shortnr.Web -- \
  --Database:ConnectionString="Data Source=/mnt/data/shortnr.db"
```

## Architecture

### Request flow

- **`/`** — Index page. POST shortens a URL and returns an HTMX partial with the short URL, a "Show QR" button, and an OOB swap of the recent links table.
- **`/{shortCode}`** — Redirect endpoint (minimal API). Writes a `ClickRecord` to an in-memory channel and returns `302` immediately.
- **`/dashboard`** — Dashboard page. When auth is enabled, requires authentication. Metrics, sortable search results, and recent clicks are all scoped to the signed-in user or active workspace.
- **`/dashboard/activity`** — AI activity dashboard showing MCP tool actions performed on behalf of the user.
- **`/bio/edit`** — Bio page editor with link management and theme picker.
- **`/bio/{slug}`** — Public bio page with the owner's links rendered as buttons.
- **`/settings/domains`** — Branded domain management (add, verify via file or DNS TXT record, set default, delete).
- **`/settings/workspaces`** — Team workspace management (create, invite members, manage roles, delete).
- **`/settings/api-keys`** — API key creation and revocation.
- **`/qr/{shortCode}`** — Full shareable QR page with download link.
- **`/api/qr/{shortCode}`** — Raw PNG download endpoint.
- **`/api/metrics`** — JSON endpoint consumed by the Chart.js dashboard chart. Scoped to current user/workspace when auth is enabled; returns zeros for anonymous requests.
- **`/api/v1/links`** — Versioned REST CRUD for short links with API-key auth and rate limiting.
- **`/account/login`** / **`/account/logout`** — OIDC challenge / cookie sign-out. Only registered when `Authentication:Enabled` is `true`.
- **`/workspace/switch`** — POST endpoint that sets the active workspace cookie.

### CLI (`shortnr-cli`)

The CLI wraps the `/api/v1` REST API for command-line link management:

```bash
shortnr shorten <url> [--slug <slug>] [--domain <domain>]  # Shorten a URL
shortnr list [--page <n>] [--page-size <n>]                # List your links
shortnr stats <code> [--clicks]                            # Show link statistics
shortnr delete <code> [--force]                            # Delete a link
```

Configure the CLI with an API key via environment variable or config file:

```bash
# Environment variable (takes precedence)
export SHORTNR_API_KEY=snr_...
export SHORTNR_BASE_URL=http://localhost:5156

# Or config file at ~/.shortnr/config
{ "api_key": "snr_...", "base_url": "http://localhost:5156" }
```

Build a self-contained binary:

```bash
dotnet publish src/Shortnr.Cli/Shortnr.Cli.csproj -c Release -r linux-x64 --self-contained true
```

AOT compilation is enabled in the project file for minimal binary size, but requires native toolchain prerequisites (clang/gcc) to be installed.

### Authentication

Auth wiring follows SRP via two extension classes:

- `Extensions/AuthenticationServiceExtensions.cs` — `AddOidcAuthentication()`: registers cookie + OIDC schemes and the `OnTokenValidated` user-provisioning queue write.
- `Extensions/AuthenticationEndpointExtensions.cs` — `MapAuthenticationEndpoints()`: registers `/account/login` and `/account/logout`.

Both are no-ops when `Authentication:Enabled` is `false`.

`UserIdentityService` (scoped) is the single source of truth for `IsAuthEnabled`, `ResolveOwnerUserIdAsync(ClaimsPrincipal)`, and `ResolveActiveWorkspaceContextAsync(ClaimsPrincipal)`, used by `DashboardModel`, `IndexModel`, `ActivityModel`, `DomainsModel`, `EditModel` (bio), and the `/api/metrics` handler.

### User provisioning

On successful OIDC login, `OnTokenValidated` writes a `PendingUserLogin` to an in-memory `Channel`. `UserProvisioningProcessor` (a `BackgroundService`) drains the channel and upserts a `Users` row keyed on `(Issuer, Subject)`. The request path never blocks on a DB write.

`ShortenedUrl.OwnerUserId` is set best-effort at creation time — it may be `null` for a user's very first link if provisioning hasn't completed yet.

### HTMX + Razor Pages pattern

All HTMX responses are Razor partials in `Pages/Shared/`. Handlers branch on `HX-Request` (full page vs partial) and `HX-Target` (which region is being swapped) — never on query parameters. Sort state is embedded in `hx-get` URLs baked into each rendered partial so polls preserve user-applied sort order.

### Click tracking

The redirect endpoint writes to a `Channel<ClickRecord>` (unbounded, in-memory) and returns immediately. `ClickBatchProcessor` (a `BackgroundService`) drains the channel in batches of up to 100, batch-inserts `ClickEvent` rows, and increments `ClickCount` on the parent `ShortenedUrl` in a single `SaveChangesAsync` call.

### Rate limiting

Public endpoints are rate limited per client IP, stacking a per-minute burst window with a per-day cap:

- The shorten form (`POST /`) enforces `RateLimiting:Shorten:PerMinute` / `RateLimiting:Shorten:PerDay` and rejects over-limit requests with `429`.
- The redirect endpoint (`GET /{shortCode}`) enforces `RateLimiting:Redirect:PerMinute` / `RateLimiting:Redirect:PerDay`, deliberately far more generous than the shorten limits so legitimate traffic (including viral spikes) is never throttled.

Operators expecting very high redirect volume should configure additional limiting at the reverse proxy or CDN edge. Set `RateLimiting:TrustForwardedFor` to `true` when the app is behind a proxy that forwards the client IP in `X-Forwarded-For`.

### QR codes

`QrService` wraps `QRCoder` and is registered as a singleton. It exposes `GeneratePng` (returns `byte[]`) and `GenerateDataUri` (returns a base64 data URI for inline HTML embedding). QR codes are generated on demand — nothing is stored.

## Frontend dependencies

Managed by [LibMan](https://learn.microsoft.com/en-us/aspnet/core/client-side/libman/) via `src/Shortnr.Web/libman.json`. All assets are served from `wwwroot/lib/` (gitignored). No npm, no bundler.

| Package | Version | Local path |
|---------|---------|------------|
| Pico CSS | 2.1.1 | `wwwroot/lib/pico/css/pico.min.css` |
| htmx | 2.0.4 | `wwwroot/lib/htmx/dist/htmx.min.js` |
| Chart.js | 4.4.9 | `wwwroot/lib/chartjs/dist/chart.umd.min.js` |
| Alpine.js | 3.14.9 | `wwwroot/lib/alpinejs/dist/cdn.min.js` |

To add or update a package, edit `libman.json` and run `dotnet build` (or `libman restore` if the CLI tool is installed).

## Database migrations

The database is created and migrated automatically at startup via `db.Database.Migrate()`.
SQLite and Postgres have separate migration histories, so a schema change needs a migration
added to **both** projects.

```bash
# SQLite (src/Shortnr.Data/Migrations)
dotnet ef migrations add <Name> --project src/Shortnr.Data/Shortnr.Data.csproj
dotnet ef migrations remove --project src/Shortnr.Data/Shortnr.Data.csproj

# Postgres (src/Shortnr.Data.Postgres/Migrations) — scaffold against a real Postgres,
# with Database__Provider=Postgres and Database__ConnectionString set in the environment
dotnet ef migrations add <Name> \
  --project src/Shortnr.Data.Postgres/Shortnr.Data.Postgres.csproj \
  --startup-project src/Shortnr.Web/Shortnr.Web.csproj \
  --context AppDbContext
```

Migrations are additive — never delete or rewrite one that has been committed.

## License

shortnr is licensed under the **Business Source License 1.1** (see `LICENSE`).

In plain English:

- **Free to self-host and use internally** — for your own or your organization's
  purposes, including in production.
- **Source-available** — the code is readable, auditable, and forkable; this is
  not a closed-source project.
- **Not a hosted-service license** — you may not offer shortnr to third parties
  as a hosted or managed service without a separate commercial license.
- **Auto-converts to Apache 2.0** three years after publication, after which the
  standard Apache 2.0 terms apply.

See `CONTRIBUTING.md` for how contributions are licensed. For commercial
licensing questions, contact the address in `LICENSE`.
