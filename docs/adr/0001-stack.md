# ADR-0001 — Stack

**Status:** Draft — scaffold. No decision in this file has been taken.
**Date:** — (set when accepted)
**Related:** ADR-0005 (Aspire), ADR-0006 (exercise sources), ADR-0007 (Lens trust model),
MVP spec §5.1, §12, §13.9

---

> **This is a scaffold, not an ADR.** Per the working agreement (spec §17) the stack judgment is the
> author's to write. The **Context** below is fact-checked against the repository and the spec and
> can stand as-is. Every `### D-n` heading is a decision left **open**: it states the question, the
> options, and the trade-off, and chooses nothing.
>
> When taking the ADR: answer each `D-n`, delete the residual prompts, delete this block, and set
> **Status: Accepted** with a date.

---

## Context

### What is already true in the repository

Facts, not decisions — this half of the ADR is recording, not choosing.

- `global.json` pins SDK **10.0.400**, `rollForward: latestFeature`.
- `Directory.Packages.props` uses Central Package Management with transitive pinning. Pinned today:
  `Microsoft.AspNetCore.Components.WebAssembly` / `.Server` 10.0.11, `Microsoft.Extensions.Hosting`
  10.0.11, `Aspire.Hosting.PostgreSQL` 13.5.2, OpenTelemetry 1.15.x, xUnit 2.9.3,
  NetArchTest.Rules 1.3.2.
- **EF Core and Npgsql are not pinned yet.** The persistence half of this ADR is currently a
  statement of intent that the lockfiles do not corroborate. It lands in Epic 2.
- The nine projects of §5.1 / §13.2 exist, plus `Whetstone.AppHost` and `Whetstone.ServiceDefaults`
  (ADR-0005, exempted from §5.1 by name in `LayeringTests.cs`).
- `src/Whetstone.Web/Program.cs` registers both interactive render modes; no global `@rendermode`
  (ADR-0002).
- `deploy/` contains only `db-init/01-roles.sql`. `compose.dev.yml` is deliberately absent — the
  AppHost replaced it (ADR-0005). **`deploy/compose.yml` and `deploy/Caddyfile` from §13.2 do not
  exist yet**, so §12's production story is currently undemonstrated.
- The AppHost runs `pgvector/pgvector:pg17` on host port 5433, though no §6 table has a vector
  column.

### What changed since the stack was scoped

ADR-0001 was scoped when exercises were hand-authored and the sensitive data was answer text.
Two Accepted ADRs moved the ground under it:

- **ADR-0006:** *"Corpus ingestion is a new trust boundary — the system now reads private source
  code. Every §7 control applies to the corpus, and `BeforeState` is as sensitive as
  `reviews.answer_text` for retention purposes."*
- **ADR-0007 §3:** generation and grading run in **separate processes with separate contexts**, and
  generation's outbound network access is an explicit allowlist.

Neither of those is a stack decision on its own. Both constrain one: something has to clone or query
a Git repository, something has to execute a Lens, and this ADR is where "what runs where" gets
written down.

---

## Decision

### D-1 — The baseline, and why each part of it

**To record:** .NET 10 · Blazor Web App · EF Core 10 + Npgsql · PostgreSQL 17 · self-hosted
single-host Docker Compose.

Prompts worth answering rather than asserting:

- Why Blazor Web App rather than MVC/Razor Pages plus a SPA — and note that ADR-0002 already carries
  the render-mode half of that argument, so this ADR should not restate it.
- Why EF Core rather than Dapper or raw Npgsql, given §13.9 rejects the repository pattern precisely
  so EF Core's behaviour stays visible. The two arguments have to be consistent.
- Why PostgreSQL 17 specifically — RLS with `FORCE`, `FOR UPDATE SKIP LOCKED` (§5.3), BRIN (§6.3).
  Each of those is a feature the design already depends on, which is a stronger case than
  "it's the default".
- **pgvector.** The AppHost provisions it and §12 says "pre-installed (post-MVP use)". Is that a
  decision (keep the door open for embedding search at near-zero cost) or an accident? Either
  answer is fine written down; neither is fine implied.

### D-2 — What reads a Git repository, and where does it run

The corpus is private source code (ADR-0006). This ADR must say what performs the read.

| Option | Buys | Costs |
| --- | --- | --- |
| Clone via a managed Git library in-process | Fast, offline after first fetch, no per-query network | A durable full copy of the tenant's source on the host, plus a native dependency |
| Shell out to `git` in the worker container | Trivially correct, no library to track | A process boundary to manage, and the same durable copy |
| Provider read-only API per query (OQ-9) | No durable copy | Outbound network from generation, which ADR-0007 §3 records as the **weaker** allowlist form |
| A separate ingestion process/container | Isolates the read from everything else | A fourth deployable, against §13.9's minimalism |

Questions this ADR has to close, not defer:

- If there is a clone: **where does it live, who can read it, and what is its retention?** ADR-0006
  says every §7 control applies to the corpus, so a clone on a Docker volume is in scope for §7.9
  and for `PurgeDeletedTenant`.
- Which of the four is compatible with the ADR-0007 §3 allowlist as written, and does the choice
  change that allowlist? ADR-0007 already flags this as a live interaction with OQ-9.

### D-3 — What executes a Lens, and what isolates it

ADR-0007 §3 requires generation and grading in separate processes with separate contexts. This ADR
takes the *implementation* of "separate".

- Separate **process** spawned by the worker, separate **container**, or separate **host**?
- What does the boundary actually enforce — process only, or container plus non-root, read-only root
  filesystem, dropped capabilities, `no-new-privileges` (the §12 posture applied here too)?
- Does the generation process share a `DbContext`, a connection pool, a database role, or a host with
  the grading path? Each "yes" is a hole in ADR-0007 §3 that ADR-0003 will then have to price.
- State plainly what a process boundary does **not** buy: it does not stop a Lens reading a
  connection string out of the environment, or reaching a database it can see. The boundary has to be
  named alongside what still guards it.

### D-4 — §13.9 re-examined, row by row

Reproduce the six rows and mark each **Unchanged / Restated / Reopened**, with a sentence for any
that is not Unchanged. At minimum:

- **"No message broker" needs restating.** The original argument was sized for `GradeSubmission` —
  short, idempotent, retryable. Generation is now a long-running background job over an entire
  repository. §5.3's dispatcher gives it exponential backoff and max 5 attempts, and five retries of
  a forty-minute job that dies at minute thirty-nine is a bad default rather than a resilience
  strategy. Questions: is a generation run an outbox message at all, or a first-class row with its
  own state machine and resumability? Does it get its own worker lane so grading is not starved
  behind it? **The answer is probably still "no broker" — the ADR's job is to say what would change
  that answer** (concurrent tenants × repository size × run duration), so the next person can check
  the premise against a number instead of re-deriving the argument.
- **"No microservices"** vs ADR-0007 §3's separate process. Say why a second process for an isolation
  requirement is not a microservice — no independent deployment cadence, no network partition, no
  separate data store — otherwise it reads as the rule being quietly bent.
- **"No repository pattern over `DbContext`"** against what ADR-0003 is about to require. If tenant
  scoping and corpus access end up funnelled through a wrapper anyway, this row is Restated, not
  Unchanged.

### D-5 — Dependencies this ADR must own

§13.9 is a "deliberately not used" list. ADR-0006/0007 force things *in*, and those deserve the same
one-line justification. At least: a Git access mechanism (D-2), a JSON-schema validator for Lens
output (ADR-0007 §4), `HtmlSanitizer` + the restricted Markdig pipeline (§7.5), a secret scanner for
candidate text (ADR-0007 §4), and a model-provider SDK (OQ-2, still open). Add a **"deliberately
used"** table so the two lists sit side by side.

### D-6 — Contradictions to declare in the document

Per the ticket's acceptance criteria, contradictions are stated here rather than applied silently.
Known candidates: §7.6.2's "no tools, no retrieval, no function calling" (already superseded by
ADR-0007, and named as stale there), §13.9's Aspire paragraph (superseded by ADR-0005), and §13.2's
`compose.dev.yml` (deliberately not created, per ADR-0005).

---

## Consequences

*To write once D-1…D-6 are answered.* Use ADR-0005/0006/0007's Good / Bad-and-costs / Neutral split.
Two that will land here regardless: the corpus creates a durable copy of customer source code inside
the deployment (whatever D-2 chooses, including "none" — that is also a consequence), and the
process count for §12's single host goes up by at least one.

## Alternatives considered

*To write.* The ones worth recording because they were genuinely weighed, not strawmen: a
Node/TypeScript stack, ASP.NET Core MVC with htmx, and a managed platform instead of a self-hosted
single host. The last one is the strongest alternative and deserves a real paragraph — §12's posture
(non-root, read-only rootfs, ufw, fail2ban, tested restores) is a running operational cost that a
managed platform would absorb.
