---
name: htmx-razor-partials
description: Use when working on HTMX interactions, Razor partials, dashboard polling, sort state, or server-side rendering patterns in this project. Covers the full HTMX + ASP.NET Core Razor Pages pattern: partial responses, HX-Target branching, sort-state preservation, polling, and click tracking.
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

## Preserving sort state across polls

When a table must poll for fresh data AND preserve user-applied sort, put the poll trigger on the `<table>` element inside the partial — not on the parent container div. Compute a `pollUrl` from the current sort state so each new partial carries the correct URL for subsequent polls.

```razor
@{
    var pollUrl = clickSort != "" 
        ? "/dashboard?clickSort=" + clickSort + "&clickDir=" + clickDir 
        : "/dashboard";
}

<table class="sortable"
       hx-get="@pollUrl"
       hx-trigger="every 5s"
       hx-target="#recent-clicks"
       hx-swap="innerHTML">
    ...
</table>
```

The parent container only needs `hx-trigger="load"` for the initial fetch:

```html
<div id="recent-clicks" hx-get="/dashboard" hx-trigger="load" hx-swap="innerHTML">
    <article aria-busy="true"><p>Loading clicks...</p></article>
</div>
```

When a header sort click swaps in a new table, the new table's `hx-trigger="every 5s"` starts fresh with the correct `pollUrl` baked in. No JavaScript and no parent attribute mutation required.

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

The search results `div` uses only `load` (no poll) to avoid resetting user-applied sort on a timer. Search results refresh when the user types or clicks a sort header.

## Click tracking

Clicks are tracked asynchronously via `Channel<ClickRecord>` + `ClickBatchProcessor` background service. The redirect endpoint writes to the channel and returns immediately. The processor drains the channel in batches of 100, batch-inserting `ClickEvent` rows and incrementing `ClickCount` on the parent `ShortenedUrl`.

```csharp
// In redirect endpoint — do not await, just write
_channel.Writer.TryWrite(new ClickRecord
{
    ShortCode  = shortCode,
    IpAddress  = ctx.Connection.RemoteIpAddress?.ToString(),
    UserAgent  = ctx.Request.Headers["User-Agent"],
    Referer    = ctx.Request.Headers["Referer"],
});
```

## Partial file conventions

- All HTMX-response partials live in `Pages/Shared/` as `_PartialName.cshtml`
- The `@model` directive must match the type passed to `Partial()`
- No inline HTML construction in C# handlers — always a partial file
- `_ViewImports.cshtml` must include the necessary `@using` directives for entity and model types used in partials
