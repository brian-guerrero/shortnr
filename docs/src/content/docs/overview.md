---
title: Overview
description: What shortnr is, what it does, and where to go next in the docs.
order: 0
---

# Overview

shortnr is a self-hosted URL shortener with a real-time dashboard, native QR codes, click analytics, and AI-native link management. It runs from a single Docker command and stores everything in your own database.

## What's in the box

- **Short links** &mdash; random 6-character codes or custom vanity slugs, with per-domain uniqueness.
- **Click analytics** &mdash; batched, non-blocking click tracking with IP, user agent, referrer, and optional MaxMind GeoIP enrichment.
- **Real-time dashboard** &mdash; live metrics, sortable link tables, and Chart.js visualizations that poll as clicks arrive.
- **QR codes** &mdash; generated on your instance, no third-party API calls.
- **Branded domains** &mdash; add and verify custom domains via DNS TXT record or a well-known file.
- **Workspaces** &mdash; share links and domains with a team, with Owner / Editor / Viewer roles and email invites.
- **Link-in-bio pages** &mdash; a self-hosted Linktree alternative backed by your short links.
- **REST API v1** &mdash; full CRUD with API key auth, scoped permissions, rate limiting, and OpenAPI docs at `/api/docs`.
- **MCP server** &mdash; manage links and bio pages from Claude or any MCP client, over API keys or OAuth 2.1 with PKCE.
- **Webhooks** &mdash; HMAC-signed delivery for `link.created`, `link.clicked`, and `link.deleted`.
- **AI insights** &mdash; scheduled heuristics that surface patterns in your click data.
- **OIDC authentication** &mdash; works with Dex, Authentik, Okta, Auth0, or Entra ID. Turn it off entirely with one flag for personal use.

## Bring your own database

shortnr does not lock you into a single storage engine. It runs on:

- **SQLite** &mdash; the default. One file, zero configuration, and enough for the large majority of self-hosted instances.
- **PostgreSQL** &mdash; for running multiple instances behind a load balancer, or for higher sustained write volume.

Both providers run the same schema and the same features; the choice is about deployment shape, not capability. Switching is two environment variables, and the schema is created automatically on first start either way.

MySQL and MariaDB are not currently supported.

See the [database guide](/shortnr/docs/configuration/database/) for the comparison, configuration, and SQLite &rarr; Postgres migration steps.

## Where to go next

- [Getting started](/shortnr/docs/getting-started/) &mdash; running in under five minutes
- [Self-hosting](/shortnr/docs/self-hosting/) &mdash; production deployment, reverse proxy, backups
- [Configuration](/shortnr/docs/configuration/) &mdash; every available setting
- [Database](/shortnr/docs/configuration/database/) &mdash; SQLite vs Postgres
- [Architecture](/shortnr/docs/architecture/) &mdash; how it works under the hood
- [API reference](/shortnr/docs/api/) &mdash; REST API v1
- [MCP server](/shortnr/docs/mcp/) &mdash; AI client integration
