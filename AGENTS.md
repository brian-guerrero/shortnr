# AGENTS.md — shortnr

URL shortener with a dashboard. .NET minimal APIs, HTMX frontend, EF Core + SQLite.

## Project state

Six projects under `src/` and `tests/`:
- **Shortnr.Data** — class library: entities (`ShortenedUrl`, `ClickEvent`, `User`, `ApiKey`, `Domain`), `AppDbContext`, EF Core migrations (SQLite)
- **Shortnr.Web** — ASP.NET Core Razor Pages (`Pages/`), plus minimal API endpoints: redirect, JSON metrics/QR, a versioned REST API (`/api/v1`), and branded-domain support (`/settings/domains`). OIDC login/signup wired against a test IdP (Dex); API keys are a separate bearer-auth scheme. Auth is opt-in via `Authentication:Enabled`.
- **Shortnr.AppHost** — .NET Aspire orchestrator for local dev: runs `Shortnr.Web` plus a Dex container together. See the `dotnet-aspire` skill (`.claude/skills/dotnet-aspire`).
- **Shortnr.ServiceDefaults** — shared `AddServiceDefaults()`/`MapDefaultEndpoints()` extensions (health checks, OpenTelemetry, service discovery) referenced by `Shortnr.Web`.
- **Shortnr.Tests.Unit** (`tests/`) — xUnit unit tests for services (no HTTP stack, EF Core InMemory).
- **Shortnr.Tests.Integration** (`tests/`) — xUnit integration tests using `WebApplicationFactory<Program>` with a real SQLite DB and a `TestAuthHandler` that replaces OIDC.

`dex/config.yaml` configures the Dex test IdP — see the `dex-oidc` skill (`.claude/skills/dex-oidc`) before editing it or `Shortnr.Web`'s `Authentication:Oidc:*` config.

All projects build and all tests pass.

## Dev commands

- **Build**: `dotnet build` (repo root) — also runs `libman restore` automatically via `Microsoft.Web.LibraryManager.Build`
- **Test**: `dotnet test` (repo root) — runs both unit and integration test projects
- **Run standalone** (no auth, no IdP): `dotnet run --project src\Shortnr.Web\Shortnr.Web.csproj`
- **Run under Aspire** (starts Dex too, requires a running container runtime): `dotnet run --project src\Shortnr.AppHost\Shortnr.AppHost.csproj` — opens the Aspire dashboard URL printed to the console.
- **Add migration**: `dotnet ef migrations add <Name> --project src\Shortnr.Data\Shortnr.Data.csproj`
- **Remove migration**: `dotnet ef migrations remove --project src\Shortnr.Data\Shortnr.Data.csproj`
- **Restore frontend assets manually**: `cd src\Shortnr.Web && libman restore` (requires `dotnet tool install -g Microsoft.Web.LibraryManager.Cli`)

> A running `Shortnr.Web` or `Shortnr.AppHost` process locks `bin\` outputs and makes `dotnet build` fail with `MSB3027`/`MSB3021` file-copy errors. Stop it (or check for the PID in the error message) before building/testing.

## Multi-feature workflow (stacked PRs with gh-stack)

**Whenever dealing with multiple features at a time, use `gh stack` to split the work into a stack of small, dependent PRs** — one stack per distinct feature/project, one layer (branch → PR) per logical concern (e.g. `feat/auth-data-model` → `feat/auth-api` → `feat/auth-ui`). Each PR targets the branch below it, so reviewers see only that layer's diff. Do not open one giant PR or pile unrelated features onto a single branch.

- The `gh stack` extension (`github/gh-stack`) is installed; stacked PRs on GitHub are in public preview. See the **`gh-stack` skill** (`.opencode/skills/gh-stack`) for the full non-interactive workflow, command reference, and exit-code handling.
- Branch names use this repo's existing `feat/...` convention and are used verbatim. Plan layers in dependency order (foundational changes lowest) before running `gh stack init`.
- One-time git config (avoids interactive prompts): `git config rerere.enabled true` and `git config remote.pushDefault origin` (repo currently has a single `origin` remote).
- **All `gh stack` commands must run non-interactively** or they hang: always pass branch names to `init`/`add`/`checkout`, `--auto` to `submit`, `--json` to `view`, `--yes` to `merge`.
- Standard loop: `gh stack init feat/<first-layer>` → per layer `git add`/`git commit` then `gh stack add feat/<next-layer>` → `gh stack submit --auto` → keep layers rebased with `gh stack sync` (or navigate down, commit, `gh stack rebase --upstack`) → merge the whole stack with `gh stack merge --yes [--squash]`.
- Never use `gh pr merge` on stacked PRs; never commit unrelated features into an open stack.

## Architecture & conventions

- **Razor partials for HTMX responses** — PageModel handlers that respond to HTMX requests must use the `Partial()` helper (returns `PartialViewResult`) with a `.cshtml` partial from `Pages/Shared/`. Never build HTML inline in C# code (no raw strings, no `Content()` with HTML). Never manually construct `PartialViewResult` or assign a different model type to `ViewData.Model` — use `return Partial("Shared/_PartialName", model)` instead. Full-page responses use `Page()` with layout.
- **HTMX header check** — use `Request.Headers["HX-Request"].Count > 0` to decide full page vs partial. On POST handlers, use `Partial()` to return the partial. For multiple partial targets on the same page (e.g., dashboard metrics + search), branch on `Request.Headers["HX-Target"].FirstOrDefault()` (the `id` of the target element being swapped) instead of using query parameters to differentiate partials. This keeps URLs clean and avoids polluting `OnGet` with routing query params.
- **Click tracking** — async via `Channel<ClickRecord>` + `ClickBatchProcessor` background service (`Services/ClickBatchProcessor.cs`). Redirect endpoint writes to the channel and returns immediately; the processor batch-updates SQLite.
- **DbContext** injected into handlers via DI. `IDesignTimeDbContextFactory<AppDbContext>` in `Shortnr.Data` for `dotnet ef` CLI.
- **Migrations are additive** — never delete a committed migration.
- **SQLite** — database auto-created/updated via `db.Database.Migrate()` at startup (`Program.cs`). Connection string in `appsettings.json` → `ConnectionStrings:DefaultConnection`. DB files (`.db`, `.db-shm`, `.db-wal`) gitignored.
- **Short code / slug rules** — 6 alphanumeric chars generated server-side via `ShortLinkCodes.GenerateCode()` (`Services/ShortLinkCodes.cs`). Vanity slugs are 1–64 chars of `[a-zA-Z0-9_-]`, starting with a letter/digit (`ShortLinkCodes.IsValidSlug`). Both the Index page and `/api/v1` use `ShortLinkCodes` so validation and generation stay consistent — never duplicate slug rules elsewhere. Codes are unique per domain (`DomainId`, `ShortCode` composite unique index); `DomainId == null` means the instance's own host.
- **Solution format**: `.slnx` (new .NET 10 XML-based format).
- **HTMX**: Pico CSS v2, htmx v2, Alpine.js v3, Chart.js v4 — all served locally from `wwwroot/lib/` (gitignored, restored at build time via LibMan). See `src/Shortnr.Web/libman.json` for pinned versions. Both `pico.min.css` and `pico.colors.min.css` are loaded in `_Layout.cshtml`.
- **Status colors via Pico classes** — use `pico-background-red-100` (errors), `pico-background-green-100` (success/verified/active), `pico-background-blue-100` (informational, e.g. "default" badge, created API key) instead of inline `style` background/color. No custom color style tags.
- **Alpine.js + Chart.js**: loaded only on the Dashboard page (`/dashboard`). The Chart.js component polls `/api/metrics` every 5s via Alpine.js `setInterval`. The `#metrics-summary` HTMX region polls `/dashboard` every 5s. Search queries `/dashboard` with `HX-Target: search-results`.
- **QR codes** — `QrService` (`Services/QrService.cs`) wraps `QRCoder`. The `/qr/{shortCode}` Razor Page serves a full shareable QR page; `/api/qr/{shortCode}` returns a raw PNG for download/embedding. `QrService` is registered as a singleton in DI.
- **Provider swap**: DbContext is provider-agnostic; switching to PostgreSQL = change connection string + `UseSqlite()` → `UseNpgsql()`.
- **Auth is opt-in** — controlled by `Authentication:Enabled` in config (default `true`). When `false`, no OIDC middleware is registered, `/account/login` and `/account/logout` return 404, and the nav hides the login link. Toggle it per-environment via `appsettings.{Environment}.json` or the env var `Authentication__Enabled=false`. Auth wiring is extracted to `Extensions/AuthenticationServiceExtensions.cs` and `Extensions/AuthenticationEndpointExtensions.cs`.
- **Auth** — cookie + OpenID Connect (`Microsoft.AspNetCore.Authentication.OpenIdConnect`), challenging against `Authentication:Oidc:Authority` (Dex locally). `/account/login` and `/account/logout` are minimal API endpoints. Never add IdP-specific code to `Shortnr.Web` — swapping the upstream identity source is a `dex/config.yaml` change only (see the `dex-oidc` skill).
- **User provisioning is queued, not inline** — the OIDC handler's `OnTokenValidated` event writes a `PendingUserLogin` to an unbounded `Channel<PendingUserLogin>` (`Services/UserProvisioningProcessor.cs`, mirrors the `ClickBatchProcessor` pattern); a `BackgroundService` drains it and upserts `Users` by `(Issuer, Subject)`. Login/callback requests never block on a DB write.
- **Ownership** — `ShortenedUrl.OwnerUserId` (nullable FK to `Users`) is set on creation from the current authenticated principal, best-effort: if the user's very first action follows immediately after their very first login, the provisioning queue may not have inserted their `Users` row yet, so ownership is simply left unset for that request rather than duplicating the upsert on the request path.
- **`UserIdentityService`** — scoped service (`Services/UserIdentityService.cs`) that centralises `IsAuthEnabled` and `ResolveOwnerUserIdAsync(ClaimsPrincipal)`. Injected into `DashboardModel`, `IndexModel`, the `/api/metrics` handler, and the `/api/v1` handlers. API-key principals carry the owner id directly in a marker claim, so `ResolveOwnerUserIdAsync` short-circuits the OIDC `(Issuer, Subject)` lookup. Never duplicate owner-resolution logic in page models.
- **Dashboard access control** — when auth is enabled, unauthenticated full-page requests to `/dashboard` redirect to `/`; unauthenticated HTMX partial requests return `401` (so the browser doesn't silently swap the page with a login redirect mid-poll). The Dashboard link is hidden in the nav when auth is enabled and the user is not signed in.
- **Dashboard data scoping** — all three dashboard query branches (metrics summary, recent clicks, search/link list) filter by `OwnerUserId` when auth is enabled. `/api/metrics` returns zeros for anonymous requests when auth is enabled rather than leaking all records.
- **Dashboard domain filter** — search/link list filters by `domain` query param: `default` means `DomainId == null`, otherwise matches the verified domain hostname. Filter options come from `LoadDomainOptionsAsync` (owner's domains, `default` label first).
- **Gravatar** — `Helpers/GravatarHelper.cs` generates avatar URLs via MD5 hash of the normalised email. Falls back to the mystery-person silhouette (`d=mp`). Used in `Pages/Shared/_UserMenu.cshtml`.
- **Nav user menu** — uses Pico CSS's native `<details class="dropdown">` pattern. No custom dropdown CSS. The `_UserMenu.cshtml` partial reads claims from the current principal.
- **Branded domains** — `Domain` entity (hostname, `OwnerUserId`, `IsVerified`, `IsDefault`, `VerificationToken`). Managed at `/settings/domains` (`Pages/Settings/Domains.cshtml.cs`): add (validates hostname regex), verify, set-default, delete. Verification: `DomainVerifierService` (`Services/DomainVerifierService.cs`) fetches `http://{hostname}/.well-known/shortnr-verify.txt` and compares the token — the app serves that token itself at `/.well-known/shortnr-verify.txt` (host-keyed in `ApiEndpoints.cs`), so a domain pointing at the instance verifies against its own endpoint.
- **Default domain** — one `IsDefault` per owner. The first verified domain auto-becomes default (`OnPostVerify`); the settings page offers a "Make default" action (`OnPostSetDefault`). Both call `MakeDefaultAsync` which clears the owner's other defaults **and migrates the owner's existing `DomainId == null` links onto the new default** (skipping any whose short code already exists there). Link creation (Index page and `/api/v1`) resolves the owner's default domain; new links get its `DomainId`. Dashboard's domain filter uses `default` as the label for `DomainId == null`.
- **Host-aware redirect** — `GET /{shortCode}` in `ApiEndpoints.cs` looks up the link by `(host, shortCode)`: a verified domain matching the request host resolves `DomainId == domain.Id`, otherwise it falls back to the instance's `DomainId == null` links. Same code can exist on different domains.
- **API keys** — `snr_`-prefixed, 32 random bytes, only the SHA-256 hash persisted (`ApiKeyService`). Created at `/settings/api-keys` (`Pages/Settings/ApiKeys.cshtml.cs`); the plaintext key is shown exactly once. Authenticated by `ApiKeyHandler` (`Services/ApiKeyHandler.cs`) reading `Authorization: Bearer <key>`; on success the principal carries `Users.Id` as `NameIdentifier` plus the `snr_api_key` marker claim.
- **Public API v1** — `/api/v1/links` CRUD + `/links/{code}/clicks` (`Extensions/ApiV1Endpoints.cs`). Whole group requires the `ApiKey` policy and the `api-key` rate-limiter. `CreateLinkAsync` accepts an optional `Domain` (must be owned + verified); omitted it falls back to the owner's default domain. OpenAPI is restricted to `api/v1*` paths; Scalar UI at `/api/docs`.
- **Rate limiting** — `ChainedRateLimiter` (`Services/ChainedRateLimiter.cs`) stacks two fixed windows per key: 60 req/min burst + 1000/day cap. Partitioned by hashed key (`Program.cs`), returns `429`.

## Testing conventions

- Unit tests live in `tests/Shortnr.Tests.Unit/`. Use EF Core InMemory provider. No HTTP stack.
- Integration tests live in `tests/Shortnr.Tests.Integration/`. Use `WebApplicationFactory<Program>` with `ShortnrWebAppFactory` (isolated SQLite DB per test class, auth overridden with `TestAuthHandler`).
- Control auth state in integration tests via `factory.Services.GetRequiredService<TestAuthState>().SetAuthenticatedUser(...)`. Never test actual OIDC flows — those require a running IdP and are E2E territory.
- API-key auth tests (`ApiV1EndpointsTests`) seed a `User` + `ApiKey` row directly and send `Authorization: Bearer <plaintext>` — `ApiKeyService.HashKey` must match what the handler computes. Domain verification tests (`DomainsSettingsTests`) stub `DomainVerifierService` via `ConfigureTestServices` with a fake HTTP handler serving tokens by hostname.
- `public partial class Program { }` at the bottom of `Program.cs` is required for `WebApplicationFactory<Program>` to compile.
