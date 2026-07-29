# AGENTS.md — shortnr

URL shortener with a dashboard. .NET minimal APIs, HTMX frontend, EF Core + SQLite.

## Project state

Four projects under `src/`:
- **Shortnr.Data** — class library: entities, `AppDbContext`, EF Core migrations (SQLite)
- **Shortnr.Web** — ASP.NET Core Razor Pages (`Pages/`), plus minimal API endpoints for redirect and JSON. OIDC login/signup wired against a test IdP (Dex).
- **Shortnr.AppHost** — .NET Aspire orchestrator for local dev: runs `Shortnr.Web` plus a Dex container together. See the `dotnet-aspire` skill (`.claude/skills/dotnet-aspire`).
- **Shortnr.ServiceDefaults** — shared `AddServiceDefaults()`/`MapDefaultEndpoints()` extensions (health checks, OpenTelemetry, service discovery) referenced by `Shortnr.Web`.

`dex/config.yaml` configures the Dex test IdP — see the `dex-oidc` skill (`.claude/skills/dex-oidc`) before editing it or `Shortnr.Web`'s `Authentication:Oidc:*` config.

Both build and run. No tests yet.

## Dev commands

- **Build**: `dotnet build` (repo root) — also runs `libman restore` automatically via `Microsoft.Web.LibraryManager.Build`
- **Run standalone** (no auth IdP): `dotnet run --project src\Shortnr.Web\Shortnr.Web.csproj`
- **Run under Aspire** (starts Dex too, requires a running container runtime): `dotnet run --project src\Shortnr.AppHost\Shortnr.AppHost.csproj` — opens the Aspire dashboard URL printed to the console.
- **Add migration**: `dotnet ef migrations add <Name> --project src\Shortnr.Data\Shortnr.Data.csproj`
- **Remove migration**: `dotnet ef migrations remove --project src\Shortnr.Data\Shortnr.Data.csproj`
- **Restore frontend assets manually**: `cd src\Shortnr.Web && libman restore` (requires `dotnet tool install -g Microsoft.Web.LibraryManager.Cli`)

## Architecture & conventions

- **Razor partials for HTMX responses** — PageModel handlers that respond to HTMX requests must use the `Partial()` helper (returns `PartialViewResult`) with a `.cshtml` partial from `Pages/Shared/`. Never build HTML inline in C# code (no raw strings, no `Content()` with HTML). Never manually construct `PartialViewResult` or assign a different model type to `ViewData.Model` — use `return Partial("Shared/_PartialName", model)` instead. Full-page responses use `Page()` with layout.
- **HTMX header check** — use `Request.Headers["HX-Request"].Count > 0` to decide full page vs partial. On POST handlers, use `Partial()` to return the partial. For multiple partial targets on the same page (e.g., dashboard metrics + search), branch on `Request.Headers["HX-Target"].FirstOrDefault()` (the `id` of the target element being swapped) instead of using query parameters to differentiate partials. This keeps URLs clean and avoids polluting `OnGet` with routing query params.
- **Click tracking** — async via `Channel<ClickRecord>` + `ClickBatchProcessor` background service (`Services/ClickBatchProcessor.cs`). Redirect endpoint writes to the channel and returns immediately; the processor batch-updates SQLite.
- **DbContext** injected into handlers via DI. `IDesignTimeDbContextFactory<AppDbContext>` in `Shortnr.Data` for `dotnet ef` CLI.
- **Migrations are additive** — never delete a committed migration.
- **SQLite** — database auto-created/updated via `db.Database.Migrate()` at startup (`Program.cs`). Connection string in `appsettings.json` → `ConnectionStrings:DefaultConnection`. DB files (`.db`, `.db-shm`, `.db-wal`) gitignored.
- **Short code**: 6 alphanumeric chars, generated server-side via `Random.Shared`. DB unique index.
- **Solution format**: `.slnx` (new .NET 10 XML-based format).
- **HTMX**: Pico CSS v2, htmx v2, Alpine.js v3, Chart.js v4 — all served locally from `wwwroot/lib/` (gitignored, restored at build time via LibMan). See `src/Shortnr.Web/libman.json` for pinned versions.
- **Alpine.js + Chart.js**: loaded only on the Dashboard page (`/dashboard`). The Chart.js component polls `/api/metrics` every 5s via Alpine.js `setInterval`. The `#metrics-summary` HTMX region polls `/dashboard` every 5s. Search queries `/dashboard` with `HX-Target: search-results`.
- **QR codes** — `QrService` (`Services/QrService.cs`) wraps `QRCoder`. The `/qr/{shortCode}` Razor Page serves a full shareable QR page; `/api/qr/{shortCode}` returns a raw PNG for download/embedding. `QrService` is registered as a singleton in DI.
- **Provider swap**: DbContext is provider-agnostic; switching to PostgreSQL = change connection string + `UseSqlite()` → `UseNpgsql()`.
- **Auth** — cookie + OpenID Connect (`Microsoft.AspNetCore.Authentication.OpenIdConnect`), challenging against `Authentication:Oidc:Authority` (Dex locally). `/account/login` and `/account/logout` are minimal API endpoints in `Program.cs`. Never add IdP-specific code to `Shortnr.Web` — swapping the upstream identity source is a `dex/config.yaml` change only (see the `dex-oidc` skill).
- **User provisioning is queued, not inline** — the OIDC handler's `OnTokenValidated` event writes a `PendingUserLogin` to an unbounded `Channel<PendingUserLogin>` (`Services/UserProvisioningProcessor.cs`, mirrors the `ClickBatchProcessor` pattern); a `BackgroundService` drains it and upserts `Users` by `(Issuer, Subject)`. Login/callback requests never block on a DB write.
- **Ownership** — `ShortenedUrl.OwnerUserId` (nullable FK to `Users`) is set on creation from the current authenticated principal, best-effort: if the user's very first action follows immediately after their very first login, the provisioning queue may not have inserted their `Users` row yet, so ownership is simply left unset for that request rather than duplicating the upsert on the request path.
