---
name: htmx-razor-partials
description: Use when working on HTMX interactions, Razor partials, dashboard polling, sort state, or server-side rendering patterns in this project. Covers the full HTMX + ASP.NET Core Razor Pages pattern: partial responses, HX-Target branching, sort-state preservation, SSE event-driven updates, and click tracking.
---

# HTMX + Razor Partials Pattern

## Returning partials from a PageModel handler

Always use `PageModel.Partial()` to return a partial view. Never build HTML inline in C# code, never use `Content()` with HTML, and never manually assign a model to `ViewData.Model`.

```csharp
// CORRECT
return Partial("Shared/_MyPartial", model);

// WRONG — do not do this
ViewData.Model = model;
return new PartialViewResult { ViewName = "Shared/_MyPartial" };
```

Full-page requests return `Page()`. HTMX requests return `Partial()`.

```csharp
if (Request.Headers["HX-Request"].Count > 0)
{
    return Partial("Shared/_MyPartial", model);
}
return Page();
```

## Detecting HTMX requests and branching on target

Use `Request.Headers["HX-Request"].Count > 0` to detect HTMX requests.

When a single page has multiple HTMX-swappable regions, branch on `Request.Headers["HX-Target"].FirstOrDefault()` — the `id` of the element being swapped. Do NOT use query parameters to differentiate partials.

```csharp
public async Task<IActionResult> OnGet(...)
{
    if (Request.Headers["HX-Request"].Count > 0)
    {
        var target = Request.Headers["HX-Target"].FirstOrDefault();

        if (target == "metrics-summary")
            return Partial("Shared/_DashboardMetrics", metricsModel);

        if (target == "recent-clicks")
            return Partial("Shared/_RecentClicks", clicksList);

        // default: search-results
        return Partial("Shared/_SearchResults", linksList);
    }
    return Page();
}
```

## Reading sort/filter state in a partial

**Do not use ViewData to pass sort state to partials** — it does not flow through `Partial()` reliably. Instead, read directly from `Context.Request.Query` inside the partial, since HTMX encodes sort params in the `hx-get` URL.

```razor
@{
    var linkSort = Context.Request.Query["linkSort"].FirstOrDefault() ?? "";
    var linkDir  = Context.Request.Query["linkDir"].FirstOrDefault()  ?? "";
}
```

## Sortable table headers

Use separate query parameter namespaces per table to prevent cross-table state pollution:
- Search Links table: `linkSort` / `linkDir`
- Recent Clicks table: `clickSort` / `clickDir`

Build all URLs and indicator strings as variables in the Razor code block. Do NOT use `@(ternary)` inside HTML attribute values delimited by double-quotes — the nested double-quotes break Razor parsing.

```razor
@{
    var ccUrl = "/dashboard?linkSort=clickCount&linkDir=" + (linkSort == "clickCount" && linkDir == "asc" ? "desc" : "asc");
    var ccInd = linkSort == "clickCount" ? linkDir == "asc" ? " ▲" : " ▼" : "";
}

<th hx-get="@ccUrl" hx-target="#search-results" hx-swap="innerHTML" hx-include="#search-input">Clicks@ccInd</th>
```

Each `<th>` carries:
- `hx-get` — the sort URL (column + toggled direction)
- `hx-target` — the id of the region to swap
- `hx-swap="innerHTML"` — replace inner content only
- `hx-include="#search-input"` — for the search results table, preserve the active search filter

## SSE event-driven dashboard updates (no polling)

Replaces timer-based polling (`hx-trigger="every Ns"`) with true push-based SSE. Queries only run when data actually changes.

### Architecture

```
Click visitor → /{shortCode} redirect endpoint
  └─ writes ClickRecord to Channel<ClickRecord>
       └─ ClickBatchProcessor (BackgroundService)
            ├─ reads batch from channel
            ├─ enriches (GeoIP, UA parsing)
            ├─ writes to SQLite (SaveChangesAsync)
            └─ writes to Channel<object> SSE notification channel
                 └─ /api/events SSE endpoint (await WaitToReadAsync)
                      └─ sends "metrics-update" + "geo-update" events
                           └─ htmx-ext-sse extension dispatches to elements
                                └─ hx-get="/dashboard" with HX-Target
                                     └─ DashboardModel.OnGet → Partial()
                                          └─ SQLite query (only when data changed)
```

No timers, no polling. The SSE endpoint `WaitToReadAsync` blocks until `ClickBatchProcessor` signals after a DB write. Alpine.js charts hook into the same EventSource via `htmx:sseOpen`.

### Client setup

**Layout** — enable the extension globally:
```html
<body hx-ext="sse">
    <script src="/lib/htmx/dist/htmx.min.js"></script>
    <script src="/lib/htmx-ext-sse/dist/sse.min.js"></script>
```

**Dashboard** — establish the EventSource on the dashboard wrapper (only the dashboard page gets a connection):
```html
<div sse-connect="/api/events">
    <div id="metrics-summary" hx-get="/dashboard" hx-trigger="load, sse:metrics-update" hx-swap="innerHTML">
        ...
    </div>
    ...
    <div id="recent-clicks" hx-get="/dashboard" hx-trigger="load" hx-swap="innerHTML">
        ...
    </div>
    ...
    <div id="geo-breakdown" hx-get="/dashboard" hx-trigger="load, sse:geo-update" hx-swap="innerHTML">
        ...
    </div>
</div>
```

The `hx-trigger="load"` handles the initial fetch; `sse:metrics-update` / `sse:geo-update` handles subsequent updates.

**Recent clicks table** (inside `_RecentClicks.cshtml` partial) — carries its own sort state in `pollUrl`:
```razor
@{
    var pollUrl = "/dashboard?clickSort=" + clickSort + "&clickDir=" + clickDir + "&clickLimit=" + clickLimit;
}
<table class="sortable" hx-get="@pollUrl" hx-trigger="sse:metrics-update" hx-target="#recent-clicks" hx-swap="innerHTML">
```

The table is swapped in/out by HTMX; each new partial carries the correct `pollUrl` with current sort/limit baked in. The SSE trigger `sse:metrics-update` still works on swapped content because `getClosestMatch` walks up the DOM to find the parent `sse-connect` EventSource.

### Alpine.js chart integration

Charts listen on the **same** EventSource that htmx creates, avoiding a second connection:

```javascript
function chartComponent() {
    let topChart = null;
    let countryChart = null;
    return {
        async init() {
            await this.renderCharts();
            this.listenForUpdates();
        },
        listenForUpdates() {
            var self = this;
            document.addEventListener('htmx:sseOpen', function(e) {
                e.detail.source.addEventListener('metrics-update', function() {
                    self.renderCharts();
                });
            });
        },
        async renderCharts() {
            const resp = await fetch('/api/metrics');
            // ... render Chart.js charts
        }
    };
}
```

### SSE endpoint (C# minimal API)

`/api/events` blocks on a `Channel<object>` — only sends events when `ClickBatchProcessor` signals after a DB write:

```csharp
app.MapGet("/api/events", async (HttpContext context, Channel<object> sseChannel, ILoggerFactory loggerFactory) =>
{
    context.Response.ContentType = "text/event-stream";
    context.Response.Headers["Cache-Control"] = "no-cache";
    context.Response.Headers["Connection"] = "keep-alive";
    context.Response.Headers["X-Accel-Buffering"] = "no";

    var ct = context.RequestAborted;
    while (await sseChannel.Reader.WaitToReadAsync(ct))
    {
        while (sseChannel.Reader.TryRead(out _)) { }  // drain
        await context.Response.WriteAsync("event: data-update\ndata: \n\n");
        await context.Response.Body.FlushAsync();
    }
});
```

### ClickBatchProcessor notification

After `SaveChangesAsync`, write to the SSE channel to wake up all connected SSE clients:

```csharp
await db.SaveChangesAsync(stoppingToken);
_sseChannel.Writer.TryWrite(new object());  // notify SSE clients
buffer.Clear();
```

### Key points

- **`hx-ext="sse"` on `<body>`** — enables SSE extension globally so swapped content (recent-clicks table, geo-breakdown) is automatically processed. The extension's `htmx:afterProcessNode` handler calls `registerSSE()` on newly swapped elements, which calls `getClosestMatch` to find the nearest ancestor with a `sseEventSource` (the dashboard wrapper).
- **`sse-connect` on dashboard wrapper only** — the EventSource is only created for the dashboard page, not other pages.
- **`htmx-ext-sse@2.2.4` from `jsdelivr`** — the v2-specific extension package (NOT the broken v1 compatibility file bundled in `htmx.org`). Uses `getSelectors()`, proper `source.addEventListener()` for `hx-trigger="sse:*"`, and dispatches `htmx.trigger()`.
- **Notification channel**: `ClickBatchProcessor` writes to `Channel<object>` after the DB transaction commits. The SSE endpoint blocks on `WaitToReadAsync` — no events sent when nothing changes.
- **Initial load**: `hx-trigger="load"` on each region fetches data once on page render. After that, only SSE events trigger updates.
- **Charts**: Alpine.js hooks into the same EventSource via `htmx:sseOpen` event — no `setInterval`, no second connection.

## Per-page results dropdown

Include `clickLimit` in every URL to preserve state across SSE-triggered refreshes:

```razor
@{
    var pollUrl = "/dashboard?clickSort=" + clickSort + "&clickDir=" + clickDir + "&clickLimit=" + clickLimit;
}
```

The dropdown `<select>` uses `hx-get` without `clickLimit` (htmx appends the selected value automatically):
```html
<select id="per-page" name="clickLimit"
        hx-get="/dashboard?clickSort=@clickSort&clickDir=@clickDir"
        hx-trigger="change"
        hx-target="#recent-clicks"
        hx-swap="innerHTML">
    <option value="5" selected="@(clickLimit == "5")">5</option>
    <option value="10" selected="@(clickLimit == "10")">10</option>
    <option value="15" selected="@(clickLimit == "15")">15</option>
    <option value="20" selected="@(clickLimit == "20")">20</option>
</select>
```

## Search input with HTMX

Give the search input a stable `id` so it can be referenced via `hx-include`:

```html
<input type="search" id="search-input" name="search"
       hx-get="/dashboard"
       hx-trigger="keyup changed delay:200ms"
       hx-target="#search-results"
       hx-swap="innerHTML">
<div id="search-results" hx-get="/dashboard" hx-trigger="load" hx-swap="innerHTML">
    ...
</div>
```

The search results `div` uses only `load` (no SSE) to avoid resetting user-applied sort. Results refresh when the user types or clicks a sort header.

## Click tracking

Clicks are tracked asynchronously via `Channel<ClickRecord>` + `ClickBatchProcessor` background service. The redirect endpoint writes to the channel and returns immediately. The processor drains the channel in batches of 100, enriches (GeoIP via MaxMind GeoLite2-City, UA parsing via UAParser), batch-inserts `ClickEvent` rows and increments `ClickCount` on the parent `ShortenedUrl`, then notifies the SSE channel.

```csharp
// In redirect endpoint — do not await, just write
_channel.Writer.TryWrite(new ClickRecord
{
    ShortCode  = shortCode,
    IpAddress  = ip,
    UserAgent  = ctx.Request.Headers["User-Agent"],
    Referer    = ctx.Request.Headers["Referer"],
});

// In ClickBatchProcessor — after DB commit, notify SSE clients
await db.SaveChangesAsync(stoppingToken);
_sseChannel.Writer.TryWrite(new object());
```

## Partial file conventions

- All HTMX-response partials live in `Pages/Shared/` as `_PartialName.cshtml`
- The `@model` directive must match the type passed to `Partial()`
- No inline HTML construction in C# handlers — always a partial file
- `_ViewImports.cshtml` must include the necessary `@using` directives for entity and model types used in partials
