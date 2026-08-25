# Whetstone

Deliberate practice generated from **your own codebase**.

Point it at a repository. It reads commits, merged pull requests, ADRs and structure, and
schedules questions about the code you are responsible for but did not write — then a Socratic
tutor that never hands over the answer grades your reasoning, and FSRS decides when to ask again.
The team view reports **bus factor**: who actually understands which part of the system.

Start here:

| Document | What it holds |
| --- | --- |
| [`docs/product-thesis.md`](docs/product-thesis.md) | Why this exists and why it is not LeetCode |
| [`CONTEXT.md`](CONTEXT.md) | The domain glossary. Code and issues use these words exactly |
| [`docs/adr/`](docs/adr/) | Decisions — 0006 sourcing, 0007 Lens trust, 0008 drift |
| [`docs/agents/ticket-authoring.md`](docs/agents/ticket-authoring.md) | How tickets are written, and why they withhold |

> This project is both a product and a deliberate skill-retention exercise for its author.
> The domain core — scheduling maths, graph algorithms, tenancy, concurrency, security — is
> hand-built. See the working agreement in [`CLAUDE.md`](CLAUDE.md) and spec §17.

**Stack:** .NET 10 · Blazor Web App (Auto interactivity) · Aspire 13.5 · EF Core 10 + Npgsql · PostgreSQL 17 (pgvector)

## Prerequisites

| Tool | Install |
| --- | --- |
| .NET SDK 10.0.400 | `brew install --cask dotnet-sdk` (pinned in `global.json`) |
| Docker | OrbStack or Docker Desktop |
| gitleaks | `brew install gitleaks` |

## First run

```bash
dotnet tool restore          # aspire CLI + dotnet-ef, pinned in .config/dotnet-tools.json
dotnet restore
git config core.hooksPath hooks   # per clone: enables the gitleaks pre-commit hook
dotnet dev-certs https --trust    # once per machine; prompts for your keychain
dotnet aspire run
```

`aspire run` starts the Postgres container, the web app, and the worker, and prints a
dashboard URL. The web app is at <https://localhost:7022>, Postgres on `localhost:5433`.

## Build and test

```bash
dotnet build -c Release   # warnings are errors; zero warnings is the standing bar (NFR-8)
dotnet test -c Release
```

> **Note:** `dotnet run --project src/Whetstone.AppHost -c Release` does **not** work — the
> Aspire CLI bundle launches the AppHost from `bin/Debug` regardless of `-c`. Use
> `dotnet aspire run`, or `dotnet run --project src/Whetstone.AppHost` in Debug.

## Layout

```
src/     Domain -> Application -> Infrastructure -> Web / Worker    (enforced by ARCH-1)
         AppHost (orchestration root), ServiceDefaults (shared host wiring)
tests/   Domain / Application / Integration / Web / Ai.Evals / Architecture
deploy/  db-init/ — database roles, run on first init of the data volume
docs/    adr/ evals/ perf/ privacy/ spikes/ agents/ product-thesis.md ai-log.md
```

`Directory.Build.props` and `Directory.Packages.props` hold settings and package versions
for every project. Analyzer suppressions live in `.editorconfig`, each with a reason.

## Not done yet (Epic 0)

- **`deploy/db-init/01-roles.sql` is empty.** Until it creates `whetstone_app` and the app
  connects as that role, local development runs as the Postgres superuser, which bypasses
  every RLS policy. Ticket 0.6.
- CI pipeline (ticket 0.9), ADRs 001–005 (ticket 0.10), tag `v0.0.0` (ticket 0.11).

Tracked as GitHub issues, grouped by milestone, with build order on the
[project board](https://github.com/users/armaatus/projects/1) — `Status`, `Blockers`, `Unlocks` and
`Critical path` are derived from each issue's **Blocked by:** line by `scripts/sync-board.py`.
Every ticket is implemented by hand; see the working agreement in [`CLAUDE.md`](CLAUDE.md).
