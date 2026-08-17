---
title: Architecture
description: How shortnr works — request flow, authentication, async patterns, and the HTMX + Razor Pages architecture.
order: 6
---

# Architecture

shortnr is built with ASP.NET Core Razor Pages, HTMUX, and a multi-provider EF Core data layer. It defaults to SQLite for local development but supports PostgreSQL in production. Client-side it uses HTMX, Alpine.js, and Chart.js — with a self-contained CSS system in `site.css` that replaces Pico entirely. No client-side JavaScript framework, no Redis. External services include an optional OIDC provider (Dex for local dev), SMTP (MailPit locally), optional GeoIP enrichment, and OAuth 2.1 certificate management for the MCP server.

## Project structure

```
shortnr/
├── src/
│   ├── Shortnr.Data/               # EF Core: entities, enums, AppDbContext, SQLite migrations
│   │   ├── Entities/               # ShortenedUrl, ShortenedUrlMetadata, ShortenedUrlTag, TagSuggestion,
│   │   │                           #   ClickEvent, User, ApiKey, Domain, BioPage, BioPageLink,
│   │   │                           #   AiActivityLog, Workspace, WorkspaceMember, Webhook,
│   │   │                           #   PixelSnippet, LlmInsightRun, LlmUsageLog
│   │   └── Migrations/             # SQLite-flavored migrations
│   ├── Shortnr.Data.Postgres/      # Postgres-flavored EF Core migrations (separate history)
│   ├── Shortnr.Web/                # ASP.NET Core Razor Pages app
│   │   ├── Features/               # Feature modules (DI composition per module)
│   │   │   ├── Authentication/     # UserIdentityService, ApiKeyService, OIDC + API key handlers
│   │   │   ├── ClickTracking/      # ClickBatchProcessor, Channel<ClickRecord>
│   │   │   ├── ShortLinks/         # LinkEndpoints, QrService, ShortenRateLimiter, ViewRenderService
│   │   │   ├── Domains/            # Domain entity, verification, default domain logic
│   │   │   ├── Workspaces/         # Workspace, WorkspaceMember, WorkspaceAuthorizationService
│   │   │   ├── BioPages/           # Bio page editor + public pages
│   │   │   ├── Api/                # Versioned REST API (/api/v1)
│   │   │   ├── Mcp/                # MCP server, tools, resources, prompts, OAuth 2.1
│   │   │   ├── OAuth/              # OpenIddict OAuth 2.1 server
│   │   │   ├── Webhooks/           # Webhook dispatcher, delivery, signing
│   │   │   ├── Email/              # MailKit SMTP
│   │   │   ├── GeoIp/              # MaxMind GeoLite2 enrichment
│   │   │   ├── AiActivity/         # AiActivityProcessor, AiActivityLog
│   │   │   ├── Insights/           # AiInsightsHostedService, InsightHeuristics
│   │   │   ├── Infrastructure/     # ChainedRateLimiter, ClientIpResolver, GravatarHelper, view models
│   │   │   └── Preview/            # Link preview interstitial
│   │   ├── Pages/                  # Razor Pages + Shared partials
│   │   ├── wwwroot/                # Static files (css/site.css, lib/ restored by LibMan)
│   │   └── Program.cs              # App setup, DI, minimal API, OIDC, rate limiting
│   ├── Shortnr.Cli/                # AOT-published CLI wrapping the /api/v1 REST API
│   ├── Shortnr.AppHost/            # .NET Aspire 13.4 orchestrator (local dev)
│   └── Shortnr.ServiceDefaults/    # Shared health checks / OpenTelemetry / service discovery
├── tests/
│   ├── Shortnr.Tests.Unit/         # xUnit unit tests (EF Core InMemory)
│   └── Shortnr.Tests.Integration/  # xUnit integration tests (WebApplicationFactory, SQLite)
├── dex/
│   └── config.yaml                 # Dex (test OIDC provider) config
└── docs/                           # Astro + Starlight documentation site
```

## Request flow

- **`/`** &mdash; Index page. POST shortens a URL and returns an HTMX partial with the short URL, a "Show QR" button, and an OOB swap of the recent links table.
- **`/{shortCode}`** &mdash; Redirect endpoint (minimal API). Host-aware lookup by `(host, shortCode)`. Writes a `ClickRecord` to an in-memory channel and returns `302` immediately. Renders a pixel-tracking interstitial if a PixelSnippet is attached.
- **`/preview/{code}`** &mdash; Link preview interstitial page (shows target URL metadata before redirecting).
- **`/qr/{code}`** &mdash; QR code page rendered via `QrService`. Also serves `/api/qr/{code}` as a PNG.
- **`/dashboard`** &mdash; Dashboard page. Metrics, sortable search results, and recent clicks are scoped to the signed-in user or active workspace. Polls `/api/metrics` for charts and `/dashboard` for summaries.
- **`GET /api/metrics`** &mdash; Dashboard metrics endpoint (JSON), workspace-scoped or user-scoped.
- **`GET /api/events`** &mdash; Server-Sent Events (SSE) stream for real-time click events.
- **`/bio/{slug}`** &mdash; Public bio page with the owner's links rendered as buttons.
- **`/bio/edit`** &mdash; Bio page editor with theme picker and link reordering.
- **`/settings/domains`** &mdash; Branded domain management (add, verify, set default, delete).
- **`/settings/workspaces`** &mdash; Team workspace management (create, invite, manage roles).
- **`/settings/api-keys`** &mdash; API key creation and revocation.
- **`/settings/webhooks`** &mdash; Webhook management (url, secret, event types).
- **`/insights`** &mdash; AI-generated link suggestions surfaced from `AiActivityLog` and heuristic analysis.
- **`/dashboard/activity`** &mdash; AI activity log page (personal data only).
- **`/api/v1/links`** &mdash; Versioned REST CRUD for short links.
- **`/api/v1/links/{code}/clicks`** &mdash; Click analytics for a specific link.
- **`/api/v1/links/{code}/transfer`** &mdash; Transfer ownership of a link.
- **`/api/v1/pixel-snippets`** &mdash; Pixel snippet CRUD (REST).
- **`/api/v1` group** &mdash; Requires API key auth (`ApiKey` policy) + `api-key` rate limiter. Per-key scopes (`links:read`/`links:write`/`mcp:read`/`mcp:write`) enforced.
- **`/api/docs`** &mdash; Scalar UI for OpenAPI (restricted to `api/v1*` paths).
- **`/mcp`** &mdash; MCP server endpoint (OAuth 2.1 or API key auth).
- **`/connect/authorize`**, **`/connect/token`**, **`/connect/register`** &mdash; OAuth 2.1 endpoints for MCP clients (OpenIddict).
- **`/.well-known/shortnr-verify.txt`** &mdash; Well-known file for domain verification.
- **`/.well-known/webfinger`** &mdash; Matrix-style webfinger for MCP OAuth discovery.

## Authentication

Auth wiring follows SRP via two extension classes under `Features/Authentication/`:

- `AuthenticationServiceExtensions.cs` &mdash; registers cookie + OIDC schemes and the `OnTokenValidated` user-provisioning queue write. Also registers the API key handler and `ApiKeyService`.
- `AuthenticationEndpointExtensions.cs` &mdash; registers `/account/login` and `/account/logout` endpoints.

Both are no-ops when `Authentication:Enabled` is `false`.

`UserIdentityService` (scoped, `IUserIdentityService` abstraction) is the single source of truth for `IsAuthEnabled`, `ResolveOwnerUserIdAsync(ClaimsPrincipal)`, and `ResolveActiveWorkspaceContextAsync(ClaimsPrincipal)`.

### User provisioning

On successful OIDC login, `OnTokenValidated` writes a `PendingUserLogin` to an in-memory `Channel<PendingUserLogin>`. `UserProvisioningProcessor` (a `BackgroundService`) drains the channel and upserts a `Users` row keyed on `(Issuer, Subject)`. The request path never blocks on a DB write. Pending workspace invites matching the user's email claim are auto-accepted during provisioning.

## Click tracking

The redirect endpoint writes to a `Channel<ClickRecord>` (unbounded, in-memory) and returns immediately. `ClickBatchProcessor` (a `BackgroundService`) drains the channel in batches of up to 100, batch-inserts `ClickEvent` rows, and increments `ClickCount` on the parent `ShortenedUrl` in a single `SaveChangesAsync` call. Optional MaxMind GeoLite2 enrichment adds geolocation fields if configured.

## HTMX + Razor Pages pattern

All HTMX responses are Razor partials in `Pages/Shared/`. Handlers branch on `HX-Request` (full page vs partial) and `HX-Target` (which region is being swapped) &mdash; never on query parameters. Sort state is embedded in `hx-get` URLs baked into each rendered partial so polls preserve user-applied sort order. Pages that answer both full-page and HTMX requests set `Layout = Model.IsHtmxRequest ? null : "_Layout"` inline.

## Rate limiting

Three chained rate-limit policies are wired in `Program.cs`:

- **Shorten form** (`POST /`): `RateLimiting:Shorten:PerMinute` / `RateLimiting:Shorten:PerDay` (via `ShortenRateLimiter`).
- **Redirect** (`GET /{shortCode}`): `RateLimiting:Redirect:PerMinute` / `RateLimiting:Redirect:PerDay`, per-IP, deliberately generous.
- **API + MCP** (`/api/v1*`, `/mcp`): chained `ChainedRateLimiter` — 60 req/min burst + 1000/day cap per API key (same key budget shared across REST and MCP). MCP tools additionally rate-limited at 120/min burst + 5000/day per key.

Per-IP `X-Forwarded-For` / `X-Forwarded-Proto` support is opt-in via `RateLimiting:TrustForwardedFor` and `Hosting:TrustForwardedHeaders`.

## QR codes

`QrService` wraps `QRCoder` and is registered as a singleton. It exposes `GeneratePng` (returns `byte[]`) and `GenerateDataUri` (returns a base64 data URI for inline HTML embedding). QR codes are generated on demand &mdash; nothing is stored.

## Frontend dependencies

Managed by [LibMan](https://learn.microsoft.com/en-us/aspnet/core/client-side/libman/) via `src/Shortnr.Web/libman.json`. All non-CSS assets are served from `wwwroot/lib/` (gitignored). Styling comes from `wwwroot/css/site.css` &mdash; a self-contained design system with no external CSS framework.

| Package | Version | Local path |
|---------|---------|------------|
| site.css (custom) | n/a | `wwwroot/css/site.css` |
| htmx | 2.0.4 | `wwwroot/lib/htmx/dist/htmx.min.js` |
| htmx-ext-sse | 2.2.4 | `wwwroot/lib/htmx-ext-sse/dist/sse.ext.js` |
| Chart.js | 4.4.9 | `wwwroot/lib/chartjs/dist/chart.umd.min.js` |
| Alpine.js | 3.14.9 | `wwwroot/lib/alpinejs/dist/cdn.min.js` |

## Database

- SQLite by default, with automatic migration at startup via `db.Database.Migrate()`.
- PostgreSQL is supported via `Database:Provider=Postgres` with a connection string in `Database:ConnectionString`. The provider is selected at DI build time by `DatabaseProviderHelper.UseProvider` &mdash; `Sqlite` and `Postgres` are the only valid values (anything else throws).
- Migrations are additive and managed via `dotnet ef migrations add`. SQLite migrations live in `src/Shortnr.Data/Migrations/`; Postgres migrations in `src/Shortnr.Data.Postgres/Migrations/` (separate migration histories because providers can't share one history for the same `DbContext`). The `Shortnr.Web` project references `Shortnr.Data.Postgres` so the DLL is deployed to the output folder for runtime `Assembly.Load`, but no C# code calls it directly.
- `AppDbContext.StampTimestamps` auto-sets `CreatedAtUtc` on `Add`ed entities.

## Background services

Clicks, user provisioning, AI activity, webhook deliveries, and AI insights all follow the same `Channel<T>` + `BackgroundService` pattern:

| Service | Channel type | Trigger |
|---------|-------------|---------|
| `ClickBatchProcessor` | `Channel<ClickRecord>` | Redirect endpoint |
| `UserProvisioningProcessor` | `Channel<PendingUserLogin>` | OIDC `OnTokenValidated` |
| `AiActivityProcessor` | `Channel<AiActivityRecord>` | MCP tool execution |
| `WebhookDeliveryService` | `Channel<WebhookDeliveryRecord>` | Link events (created/clicked/deleted) |
| `AiInsightsHostedService` | Scheduled `BackgroundService` | Configurable interval (`AiInsights:AnalysisIntervalHours`, default 24h) |

The redirect endpoint and OIDC login handler write to their respective channels and return immediately &mdash; the background services never block the request path.