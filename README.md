# shortnr

A URL shortener with a real-time dashboard. Built with ASP.NET Core Razor Pages, HTMX, and SQLite.

## Features

- Shorten any URL to a 6-character code
- Duplicate URL detection — shortening the same URL returns the existing code
- Click tracking with IP, user agent, and referrer capture (async batch processing)
- Real-time dashboard with metrics, sortable link/click tables, and a Chart.js bar chart
- QR code generation — inline on the index page, shareable page at `/qr/{shortCode}`, downloadable PNG at `/api/qr/{shortCode}`
- Optional authentication — OIDC login via Dex (or any OIDC provider); disable entirely with one config flag
- Per-user dashboard — when auth is enabled, the dashboard and `/api/metrics` show only the signed-in user's links
- User menu with Gravatar avatar and sign-out dropdown in the nav
- Docker-ready with a persistent SQLite volume

## Project structure

```
shortnr/
├── src/
│   ├── Shortnr.Data/          # EF Core entities, AppDbContext, migrations
│   ├── Shortnr.Web/           # Razor Pages app
│   │   ├── Extensions/        # Auth service + endpoint extension methods
│   │   ├── Helpers/           # GravatarHelper
│   │   ├── Pages/             # Index, Dashboard, QR pages + Shared partials
│   │   ├── Services/          # ClickBatchProcessor, QrService, UserIdentityService,
│   │   │                      #   UserProvisioningProcessor
│   │   ├── Models/            # ViewModels and DTOs
│   │   ├── wwwroot/           # Static files (lib/ is gitignored, restored by LibMan)
│   │   ├── libman.json        # Frontend dependency manifest
│   │   └── Program.cs         # App setup, minimal API endpoints
│   ├── Shortnr.AppHost/       # .NET Aspire orchestrator (local dev: web app + Dex container)
│   └── Shortnr.ServiceDefaults/  # Shared health checks / OpenTelemetry / service discovery
├── tests/
│   ├── Shortnr.Tests.Unit/        # xUnit unit tests (UserIdentityService, EF InMemory)
│   └── Shortnr.Tests.Integration/ # xUnit integration tests (WebApplicationFactory + TestAuthHandler)
├── dex/
│   └── config.yaml            # Dex (test OIDC provider) config — see .claude/skills/dex-oidc
├── Dockerfile
├── .dockerignore
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

## Docker

```bash
docker build -t shortnr .
docker run -p 8080:8080 -v shortnr-data:/data shortnr
```

Open `http://localhost:8080`. The SQLite database is stored in the `shortnr-data` named volume at `/data/shortnr.db` and persists across container restarts.

## Configuration

| Setting | Default | Description |
|---------|---------|-------------|
| `ConnectionStrings__DefaultConnection` | `Data Source=shortnr.db` | SQLite connection string. Override via environment variable. |
| `ASPNETCORE_URLS` | `http://+:5000` (dev) / `http://+:8080` (Docker) | Listening address. |
| `Authentication__Enabled` | `true` | Set to `false` to disable OIDC entirely — no login UI, no access control, dashboard shows all data. |
| `Authentication__Oidc__Authority` | `http://localhost:5556/dex` | OpenID Connect issuer URL. Set automatically by `Shortnr.AppHost` when running under Aspire. |
| `Authentication__Oidc__ClientId` / `Authentication__Oidc__ClientSecret` | `shortnr-web` / dev-only value | Must match `staticClients` in `dex/config.yaml`. |

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
  --ConnectionStrings:DefaultConnection="Data Source=/mnt/data/shortnr.db"
```

## Architecture

### Request flow

- **`/`** — Index page. POST shortens a URL and returns an HTMX partial with the short URL, a "Show QR" button, and an OOB swap of the recent links table.
- **`/{shortCode}`** — Redirect endpoint (minimal API). Writes a `ClickRecord` to an in-memory channel and returns `302` immediately.
- **`/dashboard`** — Dashboard page. When auth is enabled, requires authentication (redirects to `/` for full-page requests; returns `401` for HTMX partial requests). Metrics, sortable search results, and recent clicks are all scoped to the signed-in user.
- **`/qr/{shortCode}`** — Full shareable QR page with download link.
- **`/api/qr/{shortCode}`** — Raw PNG download endpoint.
- **`/api/metrics`** — JSON endpoint consumed by the Chart.js dashboard chart. Scoped to the current user when auth is enabled; returns zeros for anonymous requests.
- **`/account/login`** / **`/account/logout`** — OIDC challenge / cookie sign-out. Only registered when `Authentication:Enabled` is `true`.

### Authentication

Auth wiring follows SRP via two extension classes:

- `Extensions/AuthenticationServiceExtensions.cs` — `AddOidcAuthentication()`: registers cookie + OIDC schemes and the `OnTokenValidated` user-provisioning queue write.
- `Extensions/AuthenticationEndpointExtensions.cs` — `MapAuthenticationEndpoints()`: registers `/account/login` and `/account/logout`.

Both are no-ops when `Authentication:Enabled` is `false`.

`UserIdentityService` (scoped) is the single source of truth for `IsAuthEnabled` and `ResolveOwnerUserIdAsync(ClaimsPrincipal)`, used by `DashboardModel`, `IndexModel`, and the `/api/metrics` handler.

### User provisioning

On successful OIDC login, `OnTokenValidated` writes a `PendingUserLogin` to an in-memory `Channel`. `UserProvisioningProcessor` (a `BackgroundService`) drains the channel and upserts a `Users` row keyed on `(Issuer, Subject)`. The request path never blocks on a DB write.

`ShortenedUrl.OwnerUserId` is set best-effort at creation time — it may be `null` for a user's very first link if provisioning hasn't completed yet.

### HTMX + Razor Pages pattern

All HTMX responses are Razor partials in `Pages/Shared/`. Handlers branch on `HX-Request` (full page vs partial) and `HX-Target` (which region is being swapped) — never on query parameters. Sort state is embedded in `hx-get` URLs baked into each rendered partial so polls preserve user-applied sort order.

### Click tracking

The redirect endpoint writes to a `Channel<ClickRecord>` (unbounded, in-memory) and returns immediately. `ClickBatchProcessor` (a `BackgroundService`) drains the channel in batches of up to 100, batch-inserts `ClickEvent` rows, and increments `ClickCount` on the parent `ShortenedUrl` in a single `SaveChangesAsync` call.

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

```bash
# Add a migration
dotnet ef migrations add <Name> --project src/Shortnr.Data/Shortnr.Data.csproj

# Remove the last migration
dotnet ef migrations remove --project src/Shortnr.Data/Shortnr.Data.csproj
```

The database is created and migrated automatically at startup via `db.Database.Migrate()`.

The `AddUsersAndOwnership` migration adds a `Users` table (keyed on `(Issuer, Subject)`,
i.e. the OIDC provider + its `sub` claim) and a nullable `ShortenedUrl.OwnerUserId` FK
linking each shortened URL to the user who created it, if any.
