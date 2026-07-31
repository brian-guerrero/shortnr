---
name: gh-stack
description: Use when working on multiple features at once and the work should be split into stacked branches/PRs via the gh-stack GitHub CLI extension. Triggers on tasks involving stacked diffs, dependent pull requests, branch chains, breaking a large feature into reviewable layers, or any "dealing with multiple features at a time" workflow. Covers creating, pushing, submitting, syncing, rebasing, navigating, restructuring, and merging stacks non-interactively.
---

# gh-stack — Stacked PRs for multiple features

`gh stack` is a [GitHub CLI](https://cli.github.com/) extension for managing **stacked branches and pull requests**. A stack is an ordered list of branches where each branch builds on the one below it, rooted on a trunk branch (typically the repo's default branch). Each branch maps to one PR whose base is the branch below it, so reviewers see only the diff for that layer.

```
main (trunk)
 └── feat/data-models     → PR #1 (base: main)
  └── feat/api-endpoints  → PR #2 (base: feat/data-models)
   └── feat/frontend      → PR #3 (base: feat/api-endpoints)  - top (furthest from trunk)
```

The **bottom** of the stack is the branch closest to the trunk, the **top** the furthest from it. Navigation (`up`, `down`, `top`, `bottom`) follows that model: `up` moves away from trunk, `down` toward it.

This project is an opencode workspace for **shortnr**. When the user is dealing with multiple features at a time, default to this workflow: split the work into a stack of small, dependent PRs instead of one large change or many parallel branches.

## When to use this skill

- Breaking a large change / multiple features into a chain of small, reviewable PRs
- Creating, rebasing, pushing, or syncing a stack of dependent branches
- Navigating between layers of a branch stack
- Viewing the status of stacked PRs
- Tearing down and rebuilding a stack to remove, reorder, or rename branches
- Any time two or more distinct features are being developed at the same time

## Prerequisites

The GitHub CLI (`gh`) v2.0+ must be installed and authenticated:

```bash
gh auth login
```

Install the extension (already installed on this machine):

```bash
gh extension install github/gh-stack
```

Before using `gh stack`, configure git to prevent interactive prompts:

```bash
git config rerere.enabled true           # remember conflict resolutions (skips prompt on init)
git config remote.pushDefault origin     # skip the remote picker if multiple remotes exist
```

**Stacked PRs are in public preview** — the GitHub repo must have stacks enabled. If a remote command exits with code 9, tell the user stacks need to be enabled on the repository before `gh stack submit` can link PRs.

## Agent rules

**All `gh stack` commands must be run non-interactively.** Every command invocation must include the flags and positional arguments needed to avoid prompts, TUIs, and interactive menus. If a command would prompt for input, it will hang indefinitely.

1. **Always supply branch names as positional arguments** to `init`, `add`, and `checkout`. Running these without arguments triggers interactive prompts. Branch names are used exactly as given — never prefixed or transformed, so `gh stack add feat/foo` creates `feat/foo`.
2. **Always use `--auto` with `gh stack submit`** to auto-generate PR titles. Without `--auto`, `submit` prompts for a title per new PR.
3. **Always use `--json` with `gh stack view`.** Without it, `view` launches an interactive TUI that cannot be operated by an agent. There is no other appropriate flag — always pass `--json`.
4. **Handle remotes.** This repo has a single remote (`origin`), so commands work by default. If a second remote appears, set `git config remote.pushDefault origin`, or pass `--remote origin` to the commands that accept it (`push`, `submit`, `sync`, `rebase`, `link`). `checkout`, `modify`, and `trunk` have no `--remote` flag — they rely on `remote.pushDefault`.
5. **Avoid branches shared across multiple stacks.** If a branch belongs to multiple stacks, commands exit with code 6. Check out a non-shared branch first.
6. **Plan stack layers by dependency order before writing code.** Foundational changes (models, migrations, shared utilities) go in lower branches; dependent changes (APIs, UI, consumers) go in higher branches. Think the chain through before running `gh stack init`.
7. **Use standard `git add` and `git commit` for staging and committing.** The `-Am` shortcut is available but should not be the default — stacked PRs are most effective when each branch contains a deliberate, logical set of changes. When starting a new layer after the first, uncommitted changes carry over to the new branch, so commit or stash before `gh stack add` if you want a clean starting point.
8. **Navigate down to change a lower layer.** If you're working on a higher layer and realize a lower layer needs changes, don't hack around it: `gh stack down` (or `gh stack checkout <branch>`), make and commit the change there, run `gh stack rebase --upstack`, then navigate back up. Otherwise the changes land in the wrong PR.
9. **Use `gh stack merge --yes` to merge stacked PRs** — `gh pr merge` does not work with stacked PRs. Scope with a PR number (`gh stack merge 42 --yes`) or stack number (`gh stack merge 7 --yes`). Choose the method with `--squash`, `--rebase`, `--merge`, or `--merge-method <method>`.
10. **Use `gh stack link` for external-tool workflows.** When branches are managed outside `gh stack` local tracking (jj, Sapling, git-town), use `gh stack link branch-a branch-b` to push, create PRs, and link a stack via the API. Provide at least two branches/PRs, or a stack number followed by arguments to append to an existing stack.

**Never do any of the following — each triggers an interactive prompt or TUI that will hang:**
- ❌ `gh stack view` or `gh stack view --short` — always `gh stack view --json`
- ❌ `gh stack submit` without `--auto`
- ❌ `gh stack init` without branch arguments
- ❌ `gh stack add` without a branch name
- ❌ `gh stack checkout` without an argument
- ❌ `gh stack checkout <pr-number>` when a different local stack already exists on those branches — this triggers an unbypassable conflict-resolution prompt; run `gh stack unstack --local` first (keeps the stack on GitHub intact), then retry

## Thinking about stack structure

Each branch in a stack should represent a **discrete, logical unit of work** that can be reviewed independently. The changes within a branch should be cohesive — they belong together and make sense as a single PR.

### Dependency chain

Stacked branches form a dependency chain: each branch builds on the one below it. **Foundational changes must go in lower (earlier) branches**; code that depends on them goes in higher (later) branches.

Plan your layers before writing code. For example, a full-stack feature in this repo might be structured as:

```
main (trunk)
 └── feat/data-models      ← Shortnr.Data entities, EF Core migration
  └── feat/api-endpoints   ← /api/v1 routes that use the models
   └── feat/frontend       ← Razor Pages / HTMX UI that calls the APIs
    └── feat/integration   ← integration tests exercising the full stack
```

These names are illustrative — choose branch names and layer boundaries that reflect the actual work. The key principle: if code in one layer depends on code in another, the dependency must be in the same branch or a lower one.

### Branch naming

Follow this repo's existing convention: feature branches are prefixed with `feat/` (e.g. `feat/prd-001-branded-domains`, `feat/prd-003-public-api`). Name each layer descriptively, e.g. `feat/auth-data-model`, `feat/auth-api`, `feat/auth-ui`. Slashes are allowed and treated as part of the name. Branch names are used verbatim by `init`/`add`.

### One stack, one story

A stack's PRs should tell a cohesive story about a feature or project; a reviewer should be able to read them in sequence, each PR a small, logical piece of the whole.

- **Single stack:** all branches are part of the same feature/project, even across concerns (models, API, frontend).
- **Separate stacks:** unrelated work — a different feature, an independent bug fix or refactor. Don't mix unrelated work into one stack just because both are in flight. Start a new stack with `gh stack init` (from the trunk) or switch with `gh stack checkout`.

Trivial incidental fixes (a typo you noticed) can ride in the current stack; if a change grows into its own project it deserves its own stack.

## Quick reference

| Task | Command |
|------|---------|
| Create a stack | `gh stack init feat/auth` |
| Create a stack of multiple branches | `gh stack init feat/auth feat/api feat/ui` |
| Adopt existing branches | `gh stack init existing-branch-a existing-branch-b` |
| Set custom trunk | `gh stack init --base develop feat/auth` |
| Add a branch to stack | `gh stack add feat/api` |
| Add branch + stage all + commit | `gh stack add -Am "message" feat/api` |
| Push branches to remote | `gh stack push` |
| Push branches + create PRs (drafts) | `gh stack submit --auto` |
| Create PRs as ready for review | `gh stack submit --auto --open` |
| Sync (fetch, rebase, push) | `gh stack sync` |
| Sync and prune merged branches | `gh stack sync --prune` |
| Rebase entire stack | `gh stack rebase` |
| Rebase upstack only | `gh stack rebase --upstack` |
| Rebase without trunk | `gh stack rebase --no-trunk` |
| Continue after conflict | `gh stack rebase --continue` |
| Abort rebase | `gh stack rebase --abort` |
| View stack details (JSON) | `gh stack view --json` |
| Switch up/down in stack | `gh stack up [n]` / `gh stack down [n]` |
| Jump to top/bottom/trunk | `gh stack top` / `gh stack bottom` / `gh stack trunk` |
| Check out by stack number | `gh stack checkout 7` |
| Check out by PR number | `gh stack checkout 42` |
| Check out by branch (local only) | `gh stack checkout feat/auth` |
| Tear down current stack to restructure | `gh stack unstack` |
| Tear down a specific stack by number | `gh stack unstack 7` |
| Link branches/PRs without local tracking | `gh stack link feat/a feat/b feat/c` |
| Merge the whole current stack | `gh stack merge --yes` |
| Merge a stack by number | `gh stack merge 7 --yes` |
| Merge up to a specific PR | `gh stack merge 42 --yes` |
| Merge with a specific method | `gh stack merge --yes --squash` |

## Workflows

### End-to-end: create a stack from scratch

```bash
# 1. Initialize a stack with the first layer
gh stack init feat/data-models
# → creates the branch and checks it out

# 2. Write code for the first layer, then stage and commit deliberately
git add src/Shortnr.Data/Entities/...
git commit -m "Add user and session models"

git add src/Shortnr.Data/Migrations/...
git commit -m "Add user table migration"

# 3. When you start a new concern, add the next branch
gh stack add feat/api-endpoints
# → creates feat/api-endpoints on top

git add src/Shortnr.Web/Extensions/...
git commit -m "Add user API routes"

# 4. Add a third layer
gh stack add feat/frontend
git add src/Shortnr.Web/Pages/...
git commit -m "Add dashboard frontend"

# ── Stack complete: feat/data-models → feat/api-endpoints → feat/frontend ──

# 5. Push everything and create PRs (drafts by default)
gh stack submit --auto

# 6. Verify
gh stack view --json
```

> **Shortcut:** `gh stack add -Am "message" feat/branch` combines staging, committing, and branch creation. Useful for single-commit layers, but it bypasses deliberate staging.

### Making mid-stack changes

When working on a higher layer and you need to change a lower layer (e.g. building frontend but need a new API endpoint), **navigate down to the correct branch, make the change there, and rebase**:

```bash
# You're on feat/frontend but need to add an endpoint

# 1. Navigate to the API branch
gh stack down
# or: gh stack checkout feat/api-endpoints

# 2. Make the change where it belongs
git add src/Shortnr.Web/Extensions/ApiV1Endpoints.cs
git commit -m "Add get-user endpoint"

# 3. Rebase everything above to pick up the change
gh stack rebase --upstack

# 4. Navigate back to where you were working
gh stack top
# or: gh stack checkout feat/frontend

# 5. Continue — the API change is now available to the frontend layer
```

If you make the API change on the frontend branch, it ends up in the wrong PR. Always put changes in the branch where they logically belong.

### Responding to review feedback

```bash
# 1. Navigate to the branch under review
gh stack checkout 42            # by PR number
# or: gh stack bottom

# 2. Make changes and commit
git add .
git commit -m "Fix auth token validation"

# 3. Rebase everything above this branch
gh stack rebase --upstack

# 4. Push the updated stack
gh stack push
```

### Routine sync after merges

```bash
# Single command: fetch, rebase, push, sync PR and stack state
gh stack sync

# Also clean up local branches for merged PRs
gh stack sync --prune
```

In non-interactive environments the prune prompt is not shown — use `--prune` explicitly. `sync` mirrors the GitHub stack locally: if PRs were added on github.com, their branches are pulled down and appended automatically. If local and remote stacks have **diverged**, sync aborts in non-interactive mode (exits 0, prints `ℹ Sync aborted — no changes were made`). Resolve a divergence by unstacking and recreating the stack.

### Handle rebase conflicts (agent workflow)

```bash
# 1. Start the rebase
gh stack rebase

# 2. On exit code 3 (conflict):
#    - Parse stderr for conflicted file paths
#    - Read those files, find <<<<<<< / ======= / >>>>>>> markers
#    - Edit files to resolve conflicts
#    - Stage the resolved files:
git add path/to/resolved-file.cs

# 3. Continue the rebase
gh stack rebase --continue

# 4. Repeat steps 2-3 for further conflicts

# 5. If unable to resolve, abort to restore everything
gh stack rebase --abort
```

### Restructure a stack (remove, reorder, rename)

Use `unstack` to tear down the stack, make structural changes, then re-init:

```bash
# 1. Remove local tracking + the GitHub stack grouping (PRs are NOT deleted)
gh stack unstack

# 2. Structural changes — e.g. rename/reorder branches
git branch -m feat/old-name feat/new-name

# 3. Re-create the stack with the new structure
gh stack init --base main feat/new-name feat/api feat/ui
```

### Parsing `gh stack view --json` output

```bash
# Get stack state as JSON
output=$(gh stack view --json)

# Check if any branch needs a rebase, and rebase if so
needs_rebase=$(echo "$output" | jq '[.branches[] | select(.needsRebase == true)] | length')
if [ "$needs_rebase" -gt 0 ]; then
  echo "Branches need rebase, rebasing stack..."
  gh stack rebase
fi

# All open PR URLs
echo "$output" | jq -r '.branches[] | select(.pr.state == "OPEN") | .pr.url'

# Merged branches
echo "$output" | jq -r '.branches[] | select(.isMerged == true) | .name'

# Current branch
echo "$output" | jq -r '.currentBranch'
```

`--json` fields per branch: `name`, `head` (SHA), `base` (parent SHA), `isCurrent`, `isMerged`, `isQueued`, `needsRebase`, `pr` (`number`, `url`, `state` — `OPEN`/`MERGED`/`QUEUED`, omitted when no PR).

## Output conventions

- **Status messages** go to **stderr** with emoji prefixes: `✓` (success), `✗` (error), `⚠` (warning), `ℹ` (info).
- **Data output** (e.g. `view --json`) goes to **stdout**.
- When piping output, use `2>/dev/null` to suppress status messages if only data output is needed.

## Exit codes and error recovery

| Code | Meaning | Agent action |
|------|---------|-------------|
| 0 | Success | Proceed normally |
| 1 | Generic error | Read stderr for details; may indicate commit/push failure |
| 2 | Not in a stack | Run `gh stack init` to create a stack first |
| 3 | Rebase conflict | Parse stderr for conflicted paths, resolve, run `gh stack rebase --continue` |
| 4 | GitHub API failure | Check `gh auth status`, retry the command |
| 5 | Invalid arguments | Fix the invocation (check flags and arguments) |
| 6 | Disambiguation required | Branch belongs to multiple stacks. `gh stack checkout <specific-branch>` to switch to a non-shared branch first |
| 7 | Rebase already in progress | Run `gh stack rebase --continue` (after resolving) or `--abort` to start over |
| 8 | Stack is locked | Another `gh stack` process is writing the stack file. Wait and retry — the lock times out after 5 seconds |
| 9 | Stacked PRs unavailable | The repository does not have stacked PRs enabled. Tell the user stacks must be enabled on the repo first |
| 10 | Modify recovery required | A `gh stack modify` session was interrupted. This skill does not use `modify`; if the repo is left in this state, run `gh stack modify --abort` |

## Known limitations

1. **Stacks are strictly linear.** Branching stacks (multiple children on a single parent) are not supported. Each branch has exactly one parent and at most one child. For parallel workstreams, use separate stacks.
2. **Stack disambiguation cannot be bypassed.** If the current branch is the trunk of multiple stacks, commands error with code 6. Check out a non-shared branch first.
3. **Multiple remotes require `--remote` or config.** Only `origin` is configured in this repo today. If another remote appears, set `remote.pushDefault`, or pass `--remote <name>` to `push`/`submit`/`sync`/`rebase`/`link`. `checkout`, `modify`, and `trunk` rely on `remote.pushDefault`.
4. **Remote stack checkout requires a stack or PR number.** `checkout` with a branch name only works with locally tracked stacks. Use a stack/PR number to pull a stack from GitHub.
5. **PR titles/bodies are auto-generated.** No flag sets a custom title/body during `submit` (generated from commits + footer). Use `gh pr edit` after creation.
6. **Merged PRs cannot be modified.** `gh stack modify` refuses branches with merged PRs; this skill avoids `modify` entirely in favor of `unstack` + `init`.
