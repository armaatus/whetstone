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
git config core.hooksPath hooks   # per clone, NOT optional: see "Secret scanning" below
dotnet dev-certs https --trust    # once per machine; prompts for your keychain

# Dev secrets — once per machine; see "Configuration and secrets" below.
# Seeds obvious placeholders into user secrets (the worker refuses to boot without
# values). Replace with real ones when the consuming feature lands.
scripts/dev-secrets.sh

dotnet aspire run
```

`aspire run` starts the Postgres container, the web app, and the worker, and prints a
dashboard URL. The web app is at <https://localhost:7022>, Postgres on `localhost:5433`.

## Configuration and secrets

Three layers, nothing else (spec §13.5): `appsettings.json` holds non-secret defaults and
structure — every secret-bearing key present with an **empty** value, so the shape is documented
and the absence is visible; `dotnet user-secrets` holds dev values, outside the repo tree, so
committing one is structurally impossible; environment variables override in production
(double underscore for nesting: `Whetstone__Ai__ApiKey`). There is no
`appsettings.Development.json` — it is gitignored *and* fails `Whetstone.Architecture.Tests`
if one appears.

Every section binds to a typed options class in `Whetstone.Application/Configuration/` with
`ValidateOnStart()`, so a missing secret crashes the process at boot — where the deploy fails
visibly — not on the first background job hours later. Each host binds only what it consumes:
the Worker takes `Whetstone:Ai`, `Whetstone:Corpus`, `Whetstone:Lens` and `Whetstone:Database`;
the Web host takes `Whetstone:Database` alone, so the AI key and the corpus token never enter
its environment (ADR-0011 §4). In development the AppHost injects the database connection
string (as `whetstone_app`); the two dev secrets are seeded once by `scripts/dev-secrets.sh`.

Until the consuming features exist (the AI key is consumed from Epic 4, the corpus token from
ingestion), the seeded values are self-describing placeholders — in the user-secrets store,
never committed, so the boot-fails-loudly guarantee still holds anywhere the script hasn't
deliberately been run. Replace one with the real value when its feature lands:

```bash
dotnet user-secrets set "Whetstone:Ai:ApiKey" "<real key>" --project src/Whetstone.Worker
dotnet user-secrets set "Whetstone:Corpus:RepoAccessToken" "<real token>" --project src/Whetstone.Worker
```

### Secret scanning

Two layers, both running `gitleaks` against the same `.gitleaks.toml` at the repo root
(spec §13.5). The hook catches a secret before it enters history, which is the only cheap
moment. The CI gate catches it when someone commits with `--no-verify`, or on a clone where
the hook path was never configured. **Once a secret is in git history, rotating it is the only
real fix** — cleaning history is not, because you do not know who cloned in between.

```bash
git config core.hooksPath hooks   # run this in every clone and every worktree
git config core.hooksPath         # should print: hooks
```

That line is per-clone git config, so nothing in the repository can set it for you. A
`hooks/pre-commit` that nobody has pointed git at scans exactly nothing, and looks identical
from the outside to one that scans everything — which is why the hook **fails loudly** when
`gitleaks` is missing rather than passing silently. A scanner that no-ops when it cannot run is
worse than no scanner, because it is believed.

`.gitleaks.toml` extends the ~180 upstream rules with four of this repo's own. Two match a
secret by its *format* — corpus repository access tokens (ADR-0006 — the credential that reads a
customer's private source) and AI provider API keys (a union of candidate formats while OQ-2 is
open). Two match it by its *position*: any non-empty value sitting in one of the four
secret-bearing configuration keys, in every syntax those keys are written in — quoted
(`"ApiKey": "…"`), environment variable (`Whetstone__Ai__ApiKey=…`), and YAML environment map
(`Whetstone__Ai__ApiKey: …`, including inside a fenced block in a Markdown file). Allowlisting is
by *value* and named to those two positional rules by id, never a blanket path: `tests/` is the
most likely place for a real token to be pasted, so excluding it would remove the rules where
they earn their keep.

Keep that file as the single source of truth for what a secret looks like here. The runtime
scan of candidate text required by ADR-0007 §4 (Epic 2.5) points at it rather than growing its
own patterns.

Lens *enablement* is deliberately not configuration: a Lens is enabled per tenant by a
`TenantAdmin` and audited (ADR-0007 §2) — database state. Configuration holds only where the
registry is and each Lens's pinned `id@version` + content hash.

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

## Releases

Deploys are tag events, never branch events (spec §13.8) — `.github/workflows/deploy.yml` fires
on `v*.*.*` and on nothing else, so a deploy is tied to a fixed reference rather than to whatever
`main` was at the time.

That the reference is fixed is not a property of tags; it is enforced, by two separate things.
The **`Release tags` repository ruleset** on `refs/tags/v*.*.*` blocks update, deletion and
non-fast-forward, so a released tag cannot be re-pointed at a different commit afterwards — this
is the part that makes the reference immutable, and `git tag -a` does nothing for it. The
**workflow** rejects a lightweight tag, so every release carries a tagger, a date and a message —
this is the part that makes it attributable. Neither substitutes for the other.

The ruleset has **no bypass actors**, the repository owner included. Creating a tag is unaffected;
moving or deleting one that has already been released is not something anyone can do by accident,
and undoing a release therefore means editing the ruleset deliberately in repository settings
rather than typing `git push --delete`. That is the intended cost: a released tag is a claim about
what was deployed, and a claim you can quietly retract is not one.

```bash
git tag -a v0.1.0 -m "..."   # annotated, from main
git push origin v0.1.0       # this is the deploy
```

`v0.0.0` is Epic 0 closed: every gate green on a foundation with **nothing built on it yet** —
build configuration, the project graph with `AssemblyMarker` types, host and options wiring, the
database roles, the architecture tests, the ADRs. No domain logic, no use case, no adapter, no
`IExerciseSource`. At that tag the deploy workflow deploys nothing and proves it fires, which is
the only moment the trigger itself can be verified in isolation. The real steps (images, migrator,
Compose over SSH, `/health` smoke check — spec §12) arrive with the epic that makes each one real.

## Status

Epic 0 is complete as of `v0.0.0`. Everything after it is tracked as GitHub issues, grouped by
milestone, with build order on the [project board](https://github.com/users/armaatus/projects/1)
— `Status`, `Blockers`, `Unlocks` and `Critical path` are derived from each issue's
**Blocked by:** line by `scripts/sync-board.py`.
Every ticket is implemented by hand; see the working agreement in [`CLAUDE.md`](CLAUDE.md).
