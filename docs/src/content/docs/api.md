---
title: API reference
description: shortnr's REST API v1 — authentication, rate limits, versioning, and how to use the interactive Scalar docs.
order: 7
---

# API reference

shortnr exposes a versioned REST API at `/api/v1` for programmatic link management. The interactive API documentation is available at `/api/docs` on any running instance (powered by Scalar/OpenAPI).

## Authentication

All `/api/v1` endpoints require an API key, sent as a bearer token:

```bash
curl -H "Authorization: Bearer snr_your_api_key_here" \
  https://your-shortnr.example.com/api/v1/links
```

### Creating an API key

1. Sign in to your shortnr instance.
2. Navigate to **Settings &rarr; API Keys**.
3. Click **Create API Key**, give it a name, and select the scopes you need.
4. Copy the key &mdash; it is shown exactly once.

### API key scopes

| Scope | Access |
|-------|--------|
| `links:read` | List and view links |
| `links:write` | Create, update, and delete links |
| `mcp:read` | Read tools via MCP |
| `mcp:write` | Write tools via MCP |

## Rate limits

API endpoints use a chained rate limiter per API key:

- **60 requests per minute** (burst window)
- **1000 requests per day** (sustained cap)

Over-limit requests receive `429 Too Many Requests`. The rate limit is partitioned by the SHA-256 hash of the API key.

## Endpoints

The full endpoint reference is available interactively at `/api/docs` on your running instance. Key endpoints include:

### Links

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/api/v1/links` | List all links (with pagination) |
| `POST` | `/api/v1/links` | Create a new short link |
| `GET` | `/api/v1/links/{code}` | Get a specific link |
| `PUT` | `/api/v1/links/{code}` | Update a link |
| `DELETE` | `/api/v1/links/{code}` | Delete a link |
| `GET` | `/api/v1/links/{code}/clicks` | Get click events for a link |

### Creating a link

```bash
curl -X POST \
  -H "Authorization: Bearer snr_your_api_key" \
  -H "Content-Type: application/json" \
  -d '{"url": "https://example.com", "customSlug": "launch"}' \
  https://your-shortnr.example.com/api/v1/links
```

The `domain` field is optional &mdash; if omitted, the owner's default verified domain is used. If specified, it must be a domain owned and verified by the API key's owner.

## Interactive documentation

Every running shortnr instance serves interactive API documentation at `/api/docs`, powered by [Scalar](https://scalar.com/) and generated from the app's OpenAPI specification.

The Scalar UI lets you:

- Browse all endpoints with request/response schemas
- Try endpoints directly from the browser
- See authentication requirements

To access it, navigate to `https://your-shortnr.example.com/api/docs` after starting your instance.

> **Note:** The interactive docs at `/api/docs` are generated from the running instance's OpenAPI spec and are always in sync with the deployed API. This page provides the conceptual overview; `/api/docs` is the authoritative endpoint reference.

## Webhooks

shortnr supports webhooks for real-time event notifications. Configure webhooks at **Settings &rarr; Webhooks** in the dashboard.

### Events

| Event | Description |
|-------|-------------|
| `link.created` | A new short link was created |
| `link.clicked` | A short link was clicked (batched through click processing) |
| `link.deleted` | A short link was deleted |

### Delivery

- Webhooks are delivered via HTTP POST with a JSON payload.
- Each delivery includes an `X-Shortnr-Signature` header containing an HMAC-SHA256 signature for payload verification.
- Failed deliveries are retried with bounded exponential backoff.
- Repeatedly failing endpoints are automatically deactivated.

### Security

The `WebhookUrlValidator` rejects unsafe/private destinations (SSRF protection) and enforces outbound URL validation. Relative URLs are rejected.
