# AGENTS.md — shortnr

URL shortener with a dashboard. .NET minimal APIs, HTMX frontend, EF Core + SQLite (with PostgreSQL as a future swap via provider pattern).

## Project state

First commit made — `src/Shortnr.Data` (EF Core + SQLite, entity, context, migration) and `src/Shortnr.Web` (ASP.NET Core minimal API with HTMX + Pico CSS) exist and build.

## Planned architecture

- **Framework**: ASP.NET Core minimal APIs for shortcode redirects
- **Frontend**: HTMX (server-rendered HTML, no SPA framework) with Pico CSS for minimal HATEOAS-friendly styling
- **Data**: EF Core with SQLite provider; design migrations/DI so swapping to Npgsql later requires only a connection string + provider change
- **Source layout**: All projects under `src/` — one web project at minimum, possibly a test project alongside

## Recurring instructions

- As you add tools, configs, or scripts, record the exact dev commands (build, test, lint, format, typecheck) below so an agent doesn't have to rediscover them.
- Keep EF Core migrations additive — never delete a migration that has been committed.
- Keep HTMX interactions stateless on the server (no ViewState, no session) to match HTMX conventions.
- Use `CreateHostBuilder` / `WebApplication.CreateBuilder` patterns consistently.

## Dev commands (discovered so far)

- **Build**: `dotnet build` (from repo root)
- **Run**: `dotnet run --project src\Shortnr.Web\Shortnr.Web.csproj`
- **Add migration**: `dotnet ef migrations add <Name> --project src\Shortnr.Data\Shortnr.Data.csproj`
- **Remove migration**: `dotnet ef migrations remove --project src\Shortnr.Data\Shortnr.Data.csproj`
