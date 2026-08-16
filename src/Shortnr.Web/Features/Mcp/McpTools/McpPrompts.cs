using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Shortnr.Web.Features.Mcp.McpTools;

/// <summary>
/// Prompt templates for guided capability discovery. Prompts are markdown templates
/// with <c>{placeholders}</c> the AI client can fill in (or present verbatim to the
/// user). They let clients self-serve onboarding: "how do I use shortnr?" resolves to
/// <c>getting_started</c>, "how do I create a bio page?" to <c>create_bio_page</c>.
/// Prompts are read-only and require the <c>mcp:read</c> scope.
/// </summary>
[McpServerPromptType]
public static class McpPrompts
{
    /// <summary>Onboarding prompt: what shortnr is, and a guided walkthrough of the
    /// core read/write tools. Mirrors the docs landing page.</summary>
    [McpServerPrompt(Name = "getting_started", Title = "Getting started with shortnr")]
    [Description("Guided onboarding prompt: what shortnr does, how to shorten a link, and how to inspect links, clicks and bio pages.")]
    public static async Task<string> GettingStarted(
        RequestContext<GetPromptRequestParams> context,
        AppDbContext db,
        UserIdentityService identity,
        CancellationToken ct = default)
    {
        var ownerUserId = await McpToolGuard.ResolveOwnerAsync(context, identity);
        if (ownerUserId is null) return McpToolGuard.OwnerError;
        if (!McpToolGuard.HasScope(context, ApiKeyScopes.McpRead))
            return $"Error: this prompt requires the '{ApiKeyScopes.McpRead}' scope.";

        var linkCount = await db.ShortenedUrls.CountAsync(l => l.OwnerUserId == ownerUserId, ct);

        return $$"""
            # Getting started with shortnr

            shortnr is a URL shortener with a link-in-bio page and click analytics. You are connected to its MCP server, which exposes tools (imperative actions), resources (structured reads) and these prompts (guided templates).

            Your account currently has {{linkCount}} short links.

            ## Shorten a link
            1. Call `create_short_link` with a destination `url`.
            2. Optionally pass a custom `custom_slug` (1-64 chars: letters, digits, '-', '_', starting with a letter or digit), a verified `domain`, and UTM tags.
            3. The tool returns the new short code.

            ## Inspect your links
            - `list_links` returns your links (paginate with `limit`/`offset`, filter with `tags`).
            - `get_link_stats` returns click counts, top referrers, devices, browsers and countries for one link.
            - `get_top_links` ranks your links by clicks over a period ('7d', '30d', '90d', 'all').
            - Resources: read `shortnr://links` to list links without a tool call, `shortnr://links/{code}` for one link's metadata, and `shortnr://analytics/{code}` for its click analytics.

            ## Manage your link-in-bio page
            - `set_bio_page_text`, `set_bio_page_theme`, `add_link_to_bio_page`, `reorder_bio_page`, `remove_link_from_bio_page`, `list_bio_page_links`.

            ## Workspaces
            - Workspace links and members are visible via `shortnr://workspaces`; switch the active workspace in the shortnr dashboard (workspaces require authentication).

            ## Scopes
            Read tools/resources/prompts need the `mcp:read` scope; mutations need `mcp:write`. Contact your shortnr administrator if a call returns a scope error.
            """;
    }

    /// <summary>Step-by-step prompt for creating a personal link-in-bio page.</summary>
    [McpServerPrompt(Name = "create_bio_page", Title = "Create a link-in-bio page")]
    [Description("Step-by-step prompt for creating a personal link-in-bio page: short links, a bio page, and the tools to build and publish it.")]
    public static async Task<string> CreateBioPage(
        RequestContext<GetPromptRequestParams> context,
        UserIdentityService identity,
        CancellationToken ct = default)
    {
        var ownerUserId = await McpToolGuard.ResolveOwnerAsync(context, identity);
        if (ownerUserId is null) return McpToolGuard.OwnerError;
        if (!McpToolGuard.HasScope(context, ApiKeyScopes.McpRead))
            return $"Error: this prompt requires the '{ApiKeyScopes.McpRead}' scope.";

        return """
            # Create a link-in-bio page

            A bio page is a single public URL (`/bio/{slug}`) that lists your short links in a chosen order and theme. Follow these steps:

            ## 1. Create the links you want to feature
            For each link on the page, call `create_short_link` with the destination `url` (and optionally a `custom_slug`). Keep the returned short codes.

            ## 2. Configure the bio page
            - `set_bio_page_text` — set your display name, a heading, and an optional bio/avatar description.
            - `set_bio_page_theme` — choose a theme from the available options.

            ## 3. Assemble the page
            - `add_link_to_bio_page` — add a link with a `title` and sort order.
            - `reorder_bio_page` — set the display order of the links (this replaces the whole ordering, so pass the full ordered list of link IDs).
            - `remove_link_from_bio_page` — drop a link from the page.

            ## 4. Review and share
            - `list_bio_page_links` — verify the final ordering and titles.
            - The page is public at `https://{host}/bio/{slug}` and doesn't require authentication to view.

            ## Tips
            - Use the page's theme picker and short link URLs for a polished result.
            - You can track how well a featured link performs with `get_link_stats`.
            """;
    }
}
