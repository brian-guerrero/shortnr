# AGENTS.md — shortnr

URL shortener with a dashboard. .NET minimal APIs, HTMX frontend, EF Core + SQLite.

## Project state

Two projects under `src/`:
- **Shortnr.Data** — class library: entities, `AppDbContext`, EF Core migrations (SQLite)
- **Shortnr.Web** — ASP.NET Core Razor Pages (`Pages/`), plus minimal API endpoints for redirect and JSON

Both build and run. No tests yet.

## Dev commands

- **Build**: `dotnet build` (repo root)
- **Run**: `dotnet run --project src\Shortnr.Web\Shortnr.Web.csproj`
- **Add migration**: `dotnet ef migrations add <Name> --project src\Shortnr.Data\Shortnr.Data.csproj`
- **Remove migration**: `dotnet ef migrations remove --project src\Shortnr.Data\Shortnr.Data.csproj`

## Architecture & conventions

- **Razor partials for HTMX responses** — PageModel handlers that respond to HTMX requests must return `PartialViewResult` with a `.cshtml` partial from `Pages/Shared/`. Never build HTML inline in C# code (no raw strings, no `Content()` with HTML). Full-page responses use `Page()` with layout; partial HTMX responses use `PartialViewResult` without layout.
- **HTMX header check** — use `Request.Headers["HX-Request"].Count > 0` to decide full page vs partial. On the page itself, set `Layout = null` for HX-Request; on POST handlers, return `PartialViewResult`.
- **Click tracking** — async via `Channel<string>` + `ClickBatchProcessor` background service (`Services/ClickBatchProcessor.cs`). Redirect endpoint writes to the channel and returns immediately; the processor batch-updates SQLite.
- **DbContext** injected into handlers via DI. `IDesignTimeDbContextFactory<AppDbContext>` in `Shortnr.Data` for `dotnet ef` CLI.
- **Migrations are additive** — never delete a committed migration.
- **SQLite** — database auto-created/updated via `db.Database.Migrate()` at startup (`Program.cs`). Connection string in `appsettings.json` → `ConnectionStrings:DefaultConnection`. DB files (`.db`, `.db-shm`, `.db-wal`) gitignored.
- **Short code**: 6 alphanumeric chars, generated server-side via `Random.Shared`. DB unique index.
- **Solution format**: `.slnx` (new .NET 10 XML-based format).
- **HTMX**: Pico CSS v2 from CDN, htmx v2 from CDN. Stateless server interactions.
- **Alpine.js + Chart.js**: loaded only on the Dashboard page (`/dashboard`). Dashboard polls `/api/metrics` every 5s. Search queries `/api/links?search=...`.
- **Provider swap**: DbContext is provider-agnostic; switching to PostgreSQL = change connection string + `UseSqlite()` → `UseNpgsql()`.
