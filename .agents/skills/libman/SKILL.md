---
name: libman
description: Use when working with LibMan (Library Manager) in this project — adding, updating, or removing frontend packages (Pico CSS, htmx, Alpine.js, Chart.js), editing libman.json, configuring auto-restore on build via Microsoft.Web.LibraryManager.Build, or understanding why wwwroot/lib/ is gitignored. Do not use for NuGet packages or npm.
---

# LibMan (Library Manager) — Frontend Asset Management

This project uses LibMan to manage client-side dependencies (Pico CSS, htmx, Alpine.js, Chart.js). There is no npm, no bundler, and no node_modules. LibMan downloads files directly from CDN providers (jsDelivr, unpkg) into `wwwroot/lib/`, which is gitignored.

## Key files

| File | Purpose |
|------|---------|
| `src/Shortnr.Web/libman.json` | Declares all frontend packages with pinned versions |
| `src/Shortnr.Web/wwwroot/lib/` | Downloaded assets — gitignored, never committed |
| `src/Shortnr.Web/Shortnr.Web.csproj` | References `Microsoft.Web.LibraryManager.Build` to auto-restore on build |

## Auto-restore on build

`Microsoft.Web.LibraryManager.Build` is a NuGet package that hooks into MSBuild. Adding it to the `.csproj` means `dotnet build` automatically runs `libman restore` before compilation — no global tool required, works in CLI, VS, and CI alike.

```xml
<PackageReference Include="Microsoft.Web.LibraryManager.Build" Version="3.0.114" />
```

This is already configured in `Shortnr.Web.csproj`. **You do not need to run `libman restore` manually before building.**

## Manual restore (optional)

If you want to restore assets without building (e.g. to get IntelliSense in the editor before first build), install the CLI tool and run:

```bash
dotnet tool install -g Microsoft.Web.LibraryManager.Cli
cd src/Shortnr.Web
libman restore
```

Note: `Microsoft.Web.LibraryManager.Cli` (global tool) and `Microsoft.Web.LibraryManager.Build` (NuGet package) are independent. The Build package does not require the CLI tool to be installed.

## libman.json format

```json
{
  "version": "3.0",
  "defaultProvider": "jsdelivr",
  "defaultDestination": "wwwroot/lib",
  "libraries": [
    {
      "library": "@picocss/pico@2.1.1",
      "destination": "wwwroot/lib/pico",
      "files": [ "css/pico.min.css" ]
    },
    {
      "library": "htmx.org@2.0.4",
      "provider": "unpkg",
      "destination": "wwwroot/lib/htmx",
      "files": [ "dist/htmx.min.js" ]
    }
  ]
}
```

- `library` — package name + exact version (e.g. `chart.js@4.4.9`). Always pin to a full semver — range syntax like `@4` is NOT supported by LibMan.
- `provider` — `jsdelivr` (default), `unpkg`, or `cdnjs`. Specify per-entry only when overriding the default.
- `destination` — path relative to project root where files are written.
- `files` — specific files to download. Omit to download all files in the package (usually too many).

## Adding a new package

Use the CLI (easiest way to get the correct syntax):

```bash
cd src/Shortnr.Web
libman install "somepackage@1.2.3" --destination wwwroot/lib/somepackage --files "dist/some.min.js"
```

This updates `libman.json` and downloads the file immediately. Then commit `libman.json` — not the downloaded files.

## Updating a package version

Edit the version string in `libman.json` directly, then run `libman restore` (or just `dotnet build`). There is no `libman update` command that auto-resolves latest.

## Current packages

| Package | Provider | Version | Local path |
|---------|----------|---------|------------|
| `@picocss/pico` | jsDelivr | 2.1.1 | `wwwroot/lib/pico/css/pico.min.css` |
| `htmx.org` | unpkg | 2.0.4 | `wwwroot/lib/htmx/dist/htmx.min.js` |
| `chart.js` | jsDelivr | 4.4.9 | `wwwroot/lib/chartjs/dist/chart.umd.min.js` |
| `alpinejs` | jsDelivr | 3.14.9 | `wwwroot/lib/alpinejs/dist/cdn.min.js` |

## HTML references

Always reference the local paths (not CDN URLs):

```html
<!-- Layout -->
<link rel="stylesheet" href="/lib/pico/css/pico.min.css">
<script src="/lib/htmx/dist/htmx.min.js"></script>

<!-- Dashboard only -->
<script src="/lib/chartjs/dist/chart.umd.min.js"></script>
<script src="/lib/alpinejs/dist/cdn.min.js"></script>
```

## Dockerfile

The Dockerfile does **not** install the LibMan CLI tool. `dotnet publish` triggers `Microsoft.Web.LibraryManager.Build` automatically, which restores `wwwroot/lib/` during the build stage — the same mechanism used locally:

```dockerfile
# dotnet publish triggers Microsoft.Web.LibraryManager.Build which runs libman restore
RUN dotnet publish Shortnr.Web/Shortnr.Web.csproj -c Release -o /app/publish
```

No `dotnet tool install`, no `libman restore` step, no PATH manipulation needed.

## .gitignore

`wwwroot/lib/` is gitignored. Commit only `libman.json`. Every developer and every CI/CD run fetches the assets fresh via `dotnet build` (which triggers `Microsoft.Web.LibraryManager.Build`).
