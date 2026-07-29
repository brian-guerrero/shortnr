---
name: dex-oidc
description: Guided implementation for running dexidp/dex as a local OpenID Connect test identity provider for shortnr — config.yaml shape, static clients/passwords, connectors, and how Shortnr.Web's OIDC handler should be configured against it. Use whenever adding/editing dex/config.yaml, the Dex container resource, or Shortnr.Web's Authentication:Oidc settings.
---

# Dex as shortnr's test OIDC provider

Reference notes for this repo specifically. [Dex](https://dexidp.io) is a small OIDC
*federation* provider: it doesn't own passwords itself unless you turn on
`enablePasswordDB`, but it always speaks standard OIDC discovery + authorization-code
flow, which is exactly what we need to exercise shortnr's login against a real,
spec-compliant IdP without depending on a real external provider (Google/GitHub/etc.) in
dev/test.

## Where it lives

- `dex/config.yaml` — Dex's runtime config, bind-mounted into the container by the
  AppHost (see the `dotnet-aspire` skill for the mount wiring).
- The Dex container resource itself is defined in `src/Shortnr.AppHost/Program.cs`.
- `Shortnr.Web` consumes it purely as an OIDC client — no Dex-specific code in the web
  app, just standard `Microsoft.AspNetCore.Authentication.OpenIdConnect` pointed at
  Dex's issuer URL.

## Minimal config.yaml shape for this repo

```yaml
issuer: http://localhost:5556/dex

storage:
  type: sqlite3
  config:
    file: /tmp/dex.db               # writable regardless of container user; state resets each restart (fine for a test IdP)

web:
  http: 0.0.0.0:5556

# Static, hardcoded OIDC client for shortnr — no dynamic client registration needed.
staticClients:
  - id: shortnr-web
    secret: dev-only-not-a-real-secret     # override via env/parameter for anything beyond local dev
    name: 'shortnr'
    redirectURIs:
      - 'http://localhost:5000/signin-oidc'
    postLogoutRedirectURIs:
      - 'http://localhost:5000/'

# `local` connector + staticPasswords = Dex acts as its own tiny user directory, so you
# can log in without wiring a real upstream IdP. Swap/add connectors (google, github,
# oidc, saml, ldap, mock) here to test other flows without touching Shortnr.Web at all —
# that's the point of using Dex: the *client* config in Shortnr.Web never changes when
# the upstream identity source changes, only this file does.
enablePasswordDB: true
staticPasswords:
  - email: "test@shortnr.local"
    # bcrypt hash of "password" — regenerate with: htpasswd -bnBC 10 "" password | tr -d ':\n'
    hash: "$2a$10$2b2cU8CPhOTaGrs1HRQuAueS7JTT5ZHsHSzYiFPm1leZck7Mc8T4W"
    username: "test"
    userID: "08a8684b-db88-4b73-90a9-3cd1661f5466"
```

Notes:
- `issuer` **must** exactly match the URL that both the browser and `Shortnr.Web`'s
  backend use to reach Dex (path included) — OIDC discovery validates the `iss` claim
  against it. Because the AppHost publishes a fixed host port (see `dotnet-aspire`
  skill), `http://localhost:5556/dex` is reachable identically from both sides in local
  dev, so there's no split issuer/internal-URL problem to work around.
- `staticClients[].redirectURIs` must exactly match `Shortnr.Web`'s configured
  `CallbackPath` (ASP.NET Core OIDC default is `/signin-oidc`) on whatever port
  `Shortnr.Web` actually listens on locally.
- Keep the `secret` here in sync with `Authentication:Oidc:ClientSecret` in
  `Shortnr.Web`'s config — for real deployments neither value belongs in source control;
  this file's secret is dev-only and fine to commit *because* Dex itself never leaves the
  local/test environment.
- To test a **different** flow (e.g. a connector-based upstream instead of the static
  password DB, or a second client with different scopes/redirects), edit this file only
  — that's the "configurable OIDC flows" the whole Dex setup exists for. Don't add
  provider-specific code to `Shortnr.Web`.

## Shortnr.Web's OIDC handler configuration

Config keys (bind to `appsettings.Development.json` / env vars, never commit real
secrets — the dev-only Dex secret above is the exception):

```json
{
  "Authentication": {
    "Oidc": {
      "Authority": "http://localhost:5556/dex",
      "ClientId": "shortnr-web",
      "ClientSecret": "dev-only-not-a-real-secret",
      "CallbackPath": "/signin-oidc"
    }
  }
}
```

`Authority` gets overridden at runtime by the AppHost's `WithEnvironment("Authentication__Oidc__Authority", dexEndpoint)`
wiring when running under Aspire — the appsettings value is just the standalone-run
fallback (`dotnet run --project src/Shortnr.Web` without the AppHost).

## Testing the flow manually

- Discovery document: `curl http://localhost:5556/dex/.well-known/openid-configuration`
- Hit `Shortnr.Web`'s login endpoint in a browser → redirected to Dex's login page →
  sign in with the static password user above → redirected back to `/signin-oidc` →
  session cookie issued.
- To test failure/edge cases (expired code, wrong redirect URI, revoked client), edit
  `dex/config.yaml` and restart just the Dex container — no `Shortnr.Web` rebuild needed.

## Common pitfalls

- Forgetting the `/dex` path segment in `issuer` (Dex serves at a sub-path by default in
  these examples) — every URL (authorize, token, jwks, redirect) inherits it from the
  discovery document, so a mismatch here breaks everything downstream, not just one
  endpoint.
- Bcrypt hash in `staticPasswords` must be for the literal password you intend to type
  in the test login form — copy-pasting the example hash without knowing it maps to
  `"password"` is a common source of confusion.
- Dex's SQLite storage file path must be inside a writable, mounted location in the
  container if you want registered/refreshed state to survive a container restart;
  otherwise every restart is a clean slate (usually desirable for a test IdP).
