# shortnr

A URL shortener with a real-time dashboard. Built with ASP.NET Core Razor Pages, HTMX, and SQLite.

## Features

- Shorten any URL to a 6-character code
- Duplicate URL detection — shortening the same URL returns the existing code
- Click tracking with IP, user agent, and referrer capture (async batch processing)
- Real-time dashboard with metrics, sortable link/click tables, and a Chart.js bar chart
- QR code generation — inline on the index page, shareable page at `/qr/{shortCode}`, downloadable PNG at `/api/qr/{shortCode}`
- Docker-ready with a persistent SQLite volume

## Project structure

```
shortnr/
├── src/
│   ├── Shortnr.Data/          # EF Core entities, AppDbContext, migrations
│   └── Shortnr.Web/           # Razor Pages app
│       ├── Pages/             # Index, Dashboard, QR pages + Shared partials
│       ├── Services/          # ClickBatchProcessor, QrService
│       ├── Models/            # ViewModels and DTOs
│       ├── wwwroot/           # Static files (lib/ is gitignored, restored by LibMan)
│       ├── libman.json        # Frontend dependency manifest
│       └── Program.cs         # App setup, minimal API endpoints
├── Dockerfile
├── .dockerignore
└── AGENTS.md
```

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Docker (optional, for containerised deployment)

## Getting started

```bash
git clone <repo-url>
cd shortnr
dotnet build        # also restores frontend assets via LibMan automatically
dotnet run --project src/Shortnr.Web/Shortnr.Web.csproj
```

Open `http://localhost:5000`.

> `dotnet build` triggers `Microsoft.Web.LibraryManager.Build`, which downloads Pico CSS, htmx, Chart.js, and Alpine.js into `wwwroot/lib/` automatically. No manual `libman restore` needed.

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

Example override:

```bash
dotnet run --project src/Shortnr.Web -- \
  --ConnectionStrings:DefaultConnection="Data Source=/mnt/data/shortnr.db"
```

## Architecture

### Request flow

- **`/`** — Index page. POST shortens a URL and returns an HTMX partial with the short URL, a "Show QR" button, and an OOB swap of the recent links table.
- **`/{shortCode}`** — Redirect endpoint (minimal API). Writes a `ClickRecord` to an in-memory channel and returns `302` immediately.
- **`/dashboard`** — Dashboard page. Metrics, sortable search results, and recent clicks — all driven by HTMX polling and header-click sort requests.
- **`/qr/{shortCode}`** — Full shareable QR page with download link.
- **`/api/qr/{shortCode}`** — Raw PNG download endpoint.
- **`/api/metrics`** — JSON endpoint consumed by the Chart.js dashboard chart.

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
