# AGENTS.md — shortnr

URL shortener with a dashboard. .NET minimal APIs, HTMX frontend, EF Core + SQLite.

## Project state

Two projects under `src/`:
- **Shortnr.Data** — class library: entities, `AppDbContext`, EF Core migrations (SQLite)
- **Shortnr.Web** — ASP.NET Core Razor Pages (`Pages/`), plus minimal API endpoints for redirect and JSON

Both build and run. No tests yet.

## Dev commands

- **Build**: `dotnet build` (repo root) — also runs `libman restore` automatically via `Microsoft.Web.LibraryManager.Build`
- **Run**: `dotnet run --project src\Shortnr.Web\Shortnr.Web.csproj`
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
