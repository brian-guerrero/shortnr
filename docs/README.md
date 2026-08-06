# shortnr docs

Documentation site for [shortnr](https://github.com/brian-guerrero/shortnr), built with [Astro](https://astro.build/).

## Local development

```bash
cd docs
npm install
npm run dev
```

Open `http://localhost:4321/shortnr/` to preview the site.

## Build

```bash
npm run build
```

The static output is written to `docs/dist/`.

## Preview production build

```bash
npm run preview
```

## Structure

```
docs/
├── src/
│   ├── content/
│   │   ├── docs/          # Documentation pages (Markdown)
│   │   └── blog/          # Blog posts (Markdown)
│   ├── layouts/           # Base layout
│   ├── pages/             # Astro pages (landing, blog index, dynamic routes)
│   ├── components/        # Nav, Footer, Head
│   └── styles/            # Global CSS with DSG-002 design tokens
├── public/                # Static assets (favicon)
├── astro.config.mjs       # Astro configuration (base: /shortnr for GitHub Pages)
└── package.json
```

## Content authoring

Documentation pages live in `src/content/docs/` as Markdown files. Each file needs frontmatter:

```yaml
---
title: Page title
description: Short description for SEO
order: 1
---
```

Blog posts live in `src/content/blog/`:

```yaml
---
title: Post title
description: Short description for SEO and social sharing
pubDate: 2026-08-05
---
```

## Deployment

The site is deployed to GitHub Pages via `.github/workflows/docs-deploy.yml` on every push to `main` that touches `docs/**`.

The site is configured with `base: "/shortnr"` for deployment at `https://<org>.github.io/shortnr/`. To deploy to a custom domain, update `site` and `base` in `astro.config.mjs`.

## Design

The site uses design tokens from DSG-002 (tempered neobrutalist): yellow accent (`#FFD23F`), violet secondary (`#7C5CFF`), Archivo Black display font, Space Grotesk body font, IBM Plex Mono for code, hard shadows, and thick borders.
