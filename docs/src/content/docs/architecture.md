---
title: Architecture
description: How shortnr works — request flow, authentication, async patterns, and the HTMX + Razor Pages architecture.
order: 4
---

# Architecture

shortnr is built with ASP.NET Core Razor Pages, HTMX, and SQLite. It uses no client-side JavaScript framework, no Redis, and no external service dependencies beyond an optional OIDC provider.

## Project structure

```
shortnr/
├── src/
│   ├── Shortnr.Data/          # EF Core entities, AppDbContext, migrations
│   ├── Shortnr.Web/           # Razor Pages app
│   │   ├── Extensions/        # Auth service + endpoint extension methods
│   │   ├── Helpers/           # GravatarHelper
│   │   ├── Pages/             # Index, Dashboard, QR, Bio, Settings pages + Shared partials
│   │   ├── Services/          # Background services, business logic
│   │   ├── Models/            # ViewModels and DTOs
│   │   ├── wwwroot/           # Static files (lib/ restored by LibMan)
│   │   └── Program.cs         # App setup, minimal API endpoints
│   ├── Shortnr.AppHost/       # .NET Aspire orchestrator (local dev)
│   └── Shortnr.ServiceDefaults/  # Shared health checks / OpenTelemetry
├── tests/
│   ├── Shortnr.Tests.Unit/        # xUnit unit tests (EF Core InMemory)
│   └── Shortnr.Tests.Integration/ # xUnit integration tests (WebApplicationFactory)
└── dex/
    └── config.yaml            # Dex (test OIDC provider) config
```

## Request flow

- **`/`** &mdash; Index page. POST shortens a URL and returns an HTMX partial with the short URL, a "Show QR" button, and an OOB swap of the recent links table.
- **`/{shortCode}`** &mdash; Redirect endpoint (minimal API). Writes a `ClickRecord` to an in-memory channel and returns `302` immediately.
- **`/dashboard`** &mdash; Dashboard page. Metrics, sortable search results, and recent clicks are scoped to the signed-in user or active workspace.
- **`/bio/{slug}`** &mdash; Public bio page with the owner's links rendered as buttons.
- **`/settings/domains`** &mdash; Branded domain management (add, verify, set default, delete).
- **`/settings/workspaces`** &mdash; Team workspace management (create, invite, manage roles).
- **`/settings/api-keys`** &mdash; API key creation and revocation.
- **`/api/v1/links`** &mdash; Versioned REST CRUD for short links with API-key auth and rate limiting.
- **`/mcp`** &mdash; MCP server endpoint for AI agent integration.

## Authentication

Auth wiring follows SRP via two extension classes:

- `Extensions/AuthenticationServiceExtensions.cs` &mdash; registers cookie + OIDC schemes and the `OnTokenValidated` user-provisioning queue write.
- `Extensions/AuthenticationEndpointExtensions.cs` &mdash; registers `/account/login` and `/account/logout`.

Both are no-ops when `Authentication:Enabled` is `false`.

`UserIdentityService` (scoped) is the single source of truth for `IsAuthEnabled`, `ResolveOwnerUserIdAsync(ClaimsPrincipal)`, and `ResolveActiveWorkspaceContextAsync(ClaimsPrincipal)`.

### User provisioning

On successful OIDC login, `OnTokenValidated` writes a `PendingUserLogin` to an in-memory `Channel`. `UserProvisioningProcessor` (a `BackgroundService`) drains the channel and upserts a `Users` row keyed on `(Issuer, Subject)`. The request path never blocks on a DB write.

## Click tracking

The redirect endpoint writes to a `Channel<ClickRecord>` (unbounded, in-memory) and returns immediately. `ClickBatchProcessor` (a `BackgroundService`) drains the channel in batches of up to 100, batch-inserts `ClickEvent` rows, and increments `ClickCount` on the parent `ShortenedUrl` in a single `SaveChangesAsync` call.

This means clicks are never lost (the channel is in-memory but the redirect returns before the write) and the redirect latency is not affected by database write time.

## HTMX + Razor Pages pattern

All HTMX responses are Razor partials in `Pages/Shared/`. Handlers branch on `HX-Request` (full page vs partial) and `HX-Target` (which region is being swapped) &mdash; never on query parameters. Sort state is embedded in `hx-get` URLs baked into each rendered partial so polls preserve user-applied sort order.

## Rate limiting

Public endpoints are rate limited per client IP, stacking a per-minute burst window with a per-day cap:

- The shorten form (`POST /`) enforces `RateLimiting:Shorten:PerMinute` / `RateLimiting:Shorten:PerDay`.
- The redirect endpoint (`GET /{shortCode}`) enforces `RateLimiting:Redirect:PerMinute` / `RateLimiting:Redirect:PerDay`, deliberately far more generous.
- API endpoints (`/api/v1`) use a chained rate limiter: 60 req/min burst + 1000/day cap per API key.

## QR codes

`QrService` wraps `QRCoder` and is registered as a singleton. It exposes `GeneratePng` (returns `byte[]`) and `GenerateDataUri` (returns a base64 data URI for inline HTML embedding). QR codes are generated on demand &mdash; nothing is stored.

## Frontend dependencies

Managed by [LibMan](https://learn.microsoft.com/en-us/aspnet/core/client-side/libman/) via `src/Shortnr.Web/libman.json`. All assets are served from `wwwroot/lib/` (gitignored). No npm, no bundler.

| Package | Version | Local path |
|---------|---------|------------|
| Pico CSS | 2.1.1 | `wwwroot/lib/pico/css/pico.min.css` |
| htmx | 2.0.4 | `wwwroot/lib/htmx/dist/htmx.min.js` |
| Chart.js | 4.4.9 | `wwwroot/lib/chartjs/dist/chart.umd.min.js` |
| Alpine.js | 3.14.9 | `wwwroot/lib/alpinejs/dist/cdn.min.js` |

## Database

- SQLite by default, with automatic migration at startup via `db.Database.Migrate()`.
- The `DbContext` is provider-agnostic &mdash; switching to PostgreSQL requires changing the connection string and replacing `UseSqlite()` with `UseNpgsql()`.
- Migrations are additive and managed via `dotnet ef migrations add`.
