# AGENTS.md — shortnr

URL shortener with a dashboard. .NET minimal APIs, HTMX frontend, EF Core + SQLite.

## Project state

Two projects under `src/`:
- **Shortnr.Data** — class library: entities, `AppDbContext`, EF Core migrations (SQLite)
- **Shortnr.Web** — ASP.NET Core minimal API (`Program.cs`), serves raw HTML via `Results.Content(html, "text/html")`

Both build and run. No tests yet.

## Dev commands

- **Build**: `dotnet build` (repo root)
- **Run**: `dotnet run --project src\Shortnr.Web\Shortnr.Web.csproj`
- **Add migration**: `dotnet ef migrations add <Name> --project src\Shortnr.Data\Shortnr.Data.csproj`
- **Remove migration**: `dotnet ef migrations remove --project src\Shortnr.Data\Shortnr.Data.csproj`

## Architecture & conventions

- **Razor partials preferred over raw strings** — even though the app is minimal API, use `AddRazorPages` / `AddControllersWithViews` and return `PartialView()` for HTMX responses. Check request headers (`X-Requested-With: XMLHttpRequest` or `HX-Request`) to decide full page vs partial. The current code uses `Results.Content()` as a placeholder; migrate to `.cshtml` partials when adding new UI.
- **DbContext** injected directly into minimal API handlers via DI. `IDesignTimeDbContextFactory<AppDbContext>` exists for `dotnet ef` CLI commands.
- **Migrations are additive** — never delete a committed migration.
- **SQLite** — database is created/updated automatically via `db.Database.Migrate()` in `Program.cs` startup (`src/Shortnr.Web/Program.cs:12-16`). Connection string in `appsettings.json` → `ConnectionStrings:DefaultConnection`.
- **SQLite DB files** (`.db`) are gitignored.
- **Short code**: 6 alphanumeric chars generated server-side via `Random.Shared`. Unique index enforced at DB level.
- **Solution format**: `.slnx` (new .NET 10 XML-based format).
- **HTMX**: Pico CSS v2 from CDN, htmx v2 from CDN. Keep interactions stateless (no session, no ViewState).
- **Provider swap**: DbContext is provider-agnostic; switching to PostgreSQL later means changing only the connection string + `UseSqlite()` → `UseNpgsql()`.
