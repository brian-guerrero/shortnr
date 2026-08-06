---
title: MCP server
description: Connect shortnr to Claude, ChatGPT, or any MCP client for AI-native link and bio page management.
order: 6
---

# MCP server

shortnr includes a built-in [Model Context Protocol](https://modelcontextprotocol.io/) (MCP) server that lets AI agents manage your links and bio pages conversationally. Connect with Claude, ChatGPT, or any MCP-compatible client.

## Overview

The MCP server exposes 12 tools across read, write, and system categories. It supports both API key authentication (for local/self-hosted clients) and OAuth 2.1 (for cloud-hosted MCP clients).

## Connection methods

### OAuth 2.1 (recommended for cloud clients)

OAuth-first MCP clients (like Claude Desktop or opencode) can connect using the standard OAuth 2.1 flow with dynamic client registration:

```bash
# In opencode
mcp auth shortnr
```

The server implements:

- **RFC 7591** &mdash; Dynamic client registration at `/connect/register`
- **Authorization + token endpoints** at `/connect/*` with PKCE required
- **RFC 9728** &mdash; Protected-resource metadata via the `Mcp` auth scheme
- **RFC 8707** &mdash; Resource binding to the `/mcp` audience
- **Refresh token flow** for persistent connections

OAuth scopes map 1:1 onto API key scopes: `mcp:read` and `mcp:write`.

### API key (for local clients)

For local or self-hosted MCP clients that support bearer authentication:

1. Create an API key at **Settings &rarr; API Keys** with `mcp:read` and/or `mcp:write` scopes.
2. Configure your MCP client to use the key as a bearer token.

The MCP endpoint is at `https://your-shortnr.example.com/mcp`.

## Available tools

### Read tools (require `mcp:read`)

| Tool | Description |
|------|-------------|
| `list_links` | List links with optional filter, tag, and sort parameters |
| `get_link_stats` | Get click count, referrers, and geo/device breakdown for a specific link |
| `get_top_links` | Get the most-clicked links for a given period |
| `list_bio_page_links` | List all links on your bio page |
| `ping` | Health check &mdash; verify the MCP connection is working |

### Write tools (require `mcp:write`)

| Tool | Description |
|------|-------------|
| `create_short_link` | Create a new short link with optional custom slug and domain |
| `update_link` | Update a link's destination, slug, or domain |
| `delete_link` | Delete a short link |
| `add_link_to_bio_page` | Add a link to your bio page |
| `remove_link_from_bio_page` | Remove a link from your bio page |
| `reorder_bio_page` | Reorder links on your bio page |
| `set_bio_page_theme` | Change your bio page theme (default, sunset, ocean, forest, midnight) |
| `set_bio_page_text` | Update your bio page display text |

### Destructive action confirmation

Write tools that modify or delete data with existing clicks or bio-page placements use a confirmation flow:

1. The tool describes what's about to happen (short code, click count, bio-page placement).
2. You confirm or decline in your AI client.
3. The action proceeds only after explicit confirmation.

You can pass `confirmed=true` to skip the confirmation prompt for scripted use.

## Example prompts

Once connected, you can manage your links conversationally:

- "Create a short link for https://github.com/brian-guerrero/shortnr with the slug `repo`"
- "What are my top 5 most-clicked links this week?"
- "Show me the stats for the `repo` link"
- "Add my three most-clicked links to my bio page"
- "Change my bio page theme to sunset"
- "Delete the link with code `aB3xY7`"

## AI Activity dashboard

All MCP tool actions are logged and visible in the AI Activity dashboard at `/dashboard/activity`. This provides an audit trail of what your AI agents have done with your links.

## Architecture

The MCP server is implemented as a stateless HTTP transport at `/mcp`. It uses the `[McpServerTool]` attribute pattern and shares the same `ApiKeyScopes` system as the REST API, so scope-check logic is uniform regardless of whether the call came from an API key or an OAuth token.

The confirmation flow is implemented in `McpToolGuard.ResolveConfirmation`, which handles three cases:

1. Explicit `confirmed=true` argument &rarr; approved immediately.
2. Echoed `InputResponses` from an already-accepted MRTR round-trip &rarr; approved/declined.
3. Neither present &rarr; throws `InputRequiredException` with an `InputRequest.ForElicitation` describing the action.
