---
title: "Self-Host a URL Shortener in 5 Minutes (No, Really)"
description: "A complete guide to running your own self-hosted URL shortener with shortnr — one Docker command, one SQLite file, real-time dashboard, native QR codes, and click analytics."
pubDate: 2026-08-05
---

If you've ever typed "self-hosted URL shortener" into a search bar, you've probably landed on one of three outcomes: a PHP project last meaningfully updated in 2019, a shortener with no real analytics beyond a raw click count, or a "modern" option that turns out to need a Postgres cluster and a Redis instance just to say hello.

shortnr is none of those. It's a self-hosted link shortener with a real-time dashboard, native QR codes, and click analytics &mdash; and it runs from a single Docker command with a single SQLite file as its database. Here's the whole setup, start to finish.

## What you'll have when you're done

- A running shortnr instance at `http://localhost:8080`
- A working short link with live click tracking
- A QR code for that link, generated with zero external services
- A real-time dashboard showing clicks as they happen

## Step 1: Run it

```bash
docker run -p 8080:8080 -v shortnr-data:/data shortnr
```

That's it. This starts shortnr with:

- Authentication disabled (fine for personal/single-user use &mdash; every link and every click is visible to whoever can reach the instance)
- A SQLite database persisted to the `shortnr-data` named Docker volume, so your links survive a container restart
- The dashboard, QR generation, and click tracking all live immediately &mdash; none of these are paid add-ons or separate services to wire up

Open `http://localhost:8080` in a browser. You should see the shortnr home page with a single input field.

## Step 2: Shorten your first link

Paste any URL into the field and submit. You'll get back a short URL immediately &mdash; something like `http://localhost:8080/aB3xY7`. Under the hood, shortnr generated a random 6-character code, checked it wasn't already in use, and wrote a row to SQLite. If you shorten the exact same URL twice, shortnr recognizes the duplicate and hands you back the existing short code instead of creating a second one &mdash; no accidental link sprawl.

Want a memorable slug instead of a random code? There's an optional custom-code field on the same form &mdash; type `launch` instead of letting shortnr generate `aB3xY7`, and (assuming `launch` isn't already taken) you'll get `http://localhost:8080/launch`.

## Step 3: Look at the QR code

Click "Show QR" next to your new short link. shortnr generates the QR code natively &mdash; no call out to a third-party QR API, no tracking pixel from someone else's service. You get an inline preview, a shareable full page at `/qr/{code}`, and a raw downloadable PNG at `/api/qr/{code}` if you need the image for print.

This matters more than it sounds like it should: most self-hosted shorteners either skip QR generation entirely or bolt it on via an external API call, which means every QR code your users scan is quietly routed through a third party's servers first. shortnr's QR codes never leave your instance.

## Step 4: Watch the dashboard

Head to `http://localhost:8080/dashboard`. This is where shortnr earns the "doesn't look like it's from 2015" claim. You'll see:

- A live-updating metrics summary (polls every 5 seconds via HTMX)
- A sortable table of all your links, with click counts
- A Chart.js bar chart of click activity
- A recent-clicks table, so you can see individual click events as they land

Click your short link a few times in another tab (or share it somewhere and let real traffic hit it) and watch the dashboard update without a manual refresh. That's the entire click-tracking pipeline working: the redirect endpoint writes a click event to an in-memory queue and returns the 302 instantly, while a background service batches and persists those events to SQLite without ever blocking the redirect itself. You get real-time-feeling analytics without paying a latency tax on every click.

## What if I want more than one person using it?

Turn on authentication. shortnr supports OIDC &mdash; meaning it works with any standard identity provider: Okta, Auth0, Azure AD, Authentik, or a self-hosted Dex instance for testing. Set `Authentication__Enabled=true` and point `Authentication__Oidc__Authority` at your IdP, and every user's dashboard, links, and click stats are automatically scoped to just their own data. No proprietary login system, no separate user database to manage by hand.

## What if I want it on my own domain?

Add a custom domain under Settings &rarr; Domains, and shortnr issues a verification token you drop in a `.well-known` file. Point your domain's DNS at your instance, verify, and new links can use `yourdomain.com/code` instead of your bare instance host &mdash; all still backed by the same SQLite file, same dashboard, same click tracking.

## Where to go from here

- The [REST API](/shortnr/docs/api/) if you want to script link creation from a deploy pipeline or another app.
- Link-in-bio pages (`/bio/edit`) if you want a Linktree-style page backed by the same short links and click data.
- The [MCP server](/shortnr/docs/mcp/), if you'd rather manage your links by talking to Claude or another AI client than clicking through a dashboard.

The whole point of shortnr is that none of these are separate products bolted together &mdash; they're one SQLite file and one small, fast web app, running wherever you decide to run it.
