# Contributing to shortnr

Thanks for considering a contribution! shortnr is a small, opinionated project —
please read this before opening a PR so your work lands cleanly.

## Ground rules

- **One PR = one logical change.** Small, reviewable diffs land faster than large
  rewrites.
- **No new dependencies without a discussion.** Every package added becomes a
  maintenance and (if shipped) licensing commitment. If you need a library, open
  an issue first to justify it.
- **Migrations are additive.** Never delete or rewrite a committed EF Core
  migration (`src/Shortnr.Data/Migrations/`).
- **Follow the architecture conventions.** HTMX responses are Razor partials
  via `Partial()` — never inline HTML in C#. Async work uses the existing
  `Channel<T>` + `BackgroundService` pattern. New HTMX surfaces branch on
  `HX-Request`/`HX-Target`, never on query params. See `AGENTS.md` for the full
  list.

## Setting up

See `README.md` — `dotnet build` (restores frontend assets via LibMan) and
`dotnet run --project src/Shortnr.Web/Shortnr.Web.csproj` for a standalone
instance. Running under Aspire also starts a local Dex IdP for auth testing.

## Development workflow

- Branch from `main` (`git worktree add -b my-feature <dir> main` is the project
  convention). There is one worktree per PRD/feature branch.
- `dotnet build` then `dotnet test` — both test projects must pass before a PR.
- Commit messages are lowercase and imperative (`Add branded domain entity`,
  `Fix redirect host resolution`).

## Tests

- **Unit** (`tests/Shortnr.Tests.Unit/`) — service logic, EF Core InMemory, no
  HTTP stack.
- **Integration** (`tests/Shortnr.Tests.Integration/`) — `WebApplicationFactory`
  with an isolated SQLite DB per test class; auth is faked via `TestAuthHandler`.

Add tests alongside any behavior change. New endpoints and new service logic are
expected to be covered.

## Licensing

shortnr is licensed under the Business Source License 1.1 (see `LICENSE`) and
converts to Apache 2.0 three years after publication. By contributing, you agree
that your contributions are licensed to the project under these terms. For
questions about commercial licensing, contact the maintainer listed in
`LICENSE`.
