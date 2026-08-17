---
title: MCP server
description: Connect shortnr to Claude, ChatGPT, or any MCP client for AI-native link and bio page management.
order: 8
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
| `list_links` | List links with optional filter, campaign, domain, status, and sort parameters |
| `get_link_stats` | Get click count, referrers, geo/device breakdown, and campaign metadata for a specific link |
| `get_top_links` | Get the most-clicked links for a given period |
| `list_pixel_snippets` | List the retargeting pixel snippets available to attach to a link |
| `list_bio_page_links` | List all links on your bio page |
| `ping` | Health check &mdash; verify the MCP connection is working |

### Write tools (require `mcp:write`)

| Tool | Description |
|------|-------------|
| `create_short_link` | Create a new short link, optionally with a custom slug/domain and campaign metadata (UTM params, retargeting pixel, iOS/Android deep links) — the natural way to spin up a distinct link per campaign |
| `update_link` | Update a link's destination, slug, domain, or campaign metadata |
| `delete_link` | Delete a short link |
| `add_link_to_bio_page` | Add a link to your bio page |
| `remove_link_from_bio_page` | Remove a link from your bio page |
| `reorder_bio_page` | Reorder links on your bio page |
| `set_bio_page_theme` | Change your bio page theme (default, sunset, ocean, forest, midnight, minimal, corporate, dark) |
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

## Deploying the OAuth server

The OAuth 2.1 authorization server (`OAuthServerExtensions.cs`) is powered by
[OpenIddict](https://documentation.openiddict.com/) and shares the process
with the rest of shortnr. It's only registered when `Authentication:Enabled`
is `true` — with auth off, `/connect/*` isn't mapped and MCP clients fall
back to API-key auth.

### Required settings

| Setting | Default | Description |
|---------|---------|-------------|
| `OAuth__Issuer` | `http://localhost:5156` | Public base URL of this deployment. Used as the OAuth issuer and in the DCR response's `registration_endpoint`. **Must match the real externally-reachable URL in production** (e.g. `https://your-shortnr.example.com`). |
| `OAuth__Resource` | `http://localhost:5156/mcp` | The MCP resource URI (RFC 8707 audience). Must match the `resource=` value MCP clients send, and is what tokens are scoped to. |
| `OAuth__AccessTokenLifetimeMinutes` | `60` | Access token lifetime. |
| `OAuth__RefreshTokenLifetimeDays` | `14` | Refresh token lifetime. |

### Signing/encryption certificates

OpenIddict needs a certificate to sign tokens and one to encrypt them.

- **In `Development`**, this is automatic — `AddDevelopmentSigningCertificate()`/`AddDevelopmentEncryptionCertificate()` generate ephemeral certs with no configuration needed.
- **Everywhere else**, you must supply persistent certificates via config/secrets. Ephemeral certs would regenerate on every process restart, silently invalidating every outstanding access/refresh token on each redeploy or cold start.

| Setting | Description |
|---------|-------------|
| `OAuth__SigningCertificate` | Base64-encoded PKCS#12 (`.pfx`) certificate used to sign tokens. Key usage must include **Digital Signature**. |
| `OAuth__SigningCertificatePassword` | Password for the signing PFX, if it has one. Optional — omit entirely if the PFX has no password. |
| `OAuth__EncryptionCertificate` | Base64-encoded PKCS#12 (`.pfx`) certificate used to encrypt tokens. Key usage must include **Key Encipherment** (and typically **Data Encipherment**) — a signing-only certificate here fails at startup with `The specified certificate is not a key encryption certificate.` |
| `OAuth__EncryptionCertificatePassword` | Password for the encryption PFX, if it has one. |

Missing either certificate throws a clear `InvalidOperationException` naming the missing config key at startup; a certificate with the wrong key usage throws the OpenIddict error above instead — both fail fast rather than serving requests with no signing/encryption key.

**Generating self-signed certs** (fine for this purpose — they're never presented to a client, only used internally to sign/encrypt opaque tokens). PowerShell:

```powershell
foreach ($pair in @(
    @{ Name = "signing";    Usage = "DigitalSignature" },
    @{ Name = "encryption"; Usage = "KeyEncipherment,DataEncipherment" }
)) {
    $cert = New-SelfSignedCertificate -Subject "CN=shortnr-oauth-$($pair.Name)" `
        -CertStoreLocation Cert:\CurrentUser\My -KeyExportPolicy Exportable `
        -KeyAlgorithm RSA -KeyLength 2048 -NotAfter (Get-Date).AddYears(5) `
        -KeyUsage $pair.Usage.Split(',')

    $pfxPath = "$($pair.Name).pfx"
    Export-PfxCertificate -Cert $cert -FilePath $pfxPath `
        -Password (New-Object System.Security.SecureString) | Out-Null
    Remove-Item "Cert:\CurrentUser\My\$($cert.Thumbprint)" -Confirm:$false

    [Convert]::ToBase64String([IO.File]::ReadAllBytes($pfxPath)) |
        Set-Content "$($pair.Name).pfx.b64" -NoNewline
}
```

Then set the secrets (Fly example — never commit these):

```
fly secrets set `
  OAuth__SigningCertificate="$(Get-Content signing.pfx.b64 -Raw)" `
  OAuth__EncryptionCertificate="$(Get-Content encryption.pfx.b64 -Raw)"
```

(No `*CertificatePassword` needed if generated exactly as above — the script uses an empty PFX password.)

### Common pitfalls

- **Wrong key usage on the encryption cert** — reusing a `DigitalSignature`-only cert for `OAuth__EncryptionCertificate` throws `The specified certificate is not a key encryption certificate.` at startup. Generate it with `KeyEncipherment`/`DataEncipherment` usage instead (see above).
- **`fly secrets set` doesn't rebuild your image** — it restarts the currently-deployed image with new env vars. If you change `OAuthServerExtensions.cs` or any other code, you need `fly deploy` too; setting secrets alone won't pick up code changes.
- **Don't duplicate secret-managed keys in `fly.toml`'s `[env]` block** — `fly deploy` re-applies `[env]` into the same machine env map that secrets populate. A value defined in both places gets silently overwritten by whichever was applied last, which is `[env]` on every deploy — so anything sensitive or environment-specific (Authority, ClientId/Secret, certs) belongs only in `fly secrets`, never mirrored into `fly.toml`.
- **`OAuth:Issuer` must be the real public HTTPS URL in production** — it's advertised in OAuth discovery metadata and the DCR `registration_endpoint`; leaving it as the `localhost` default breaks client discovery entirely once deployed.
