# ADR-0001 — Stack

**Status:** Accepted
**Date:** 2026-08-25
**Related:** ADR-0002 (render modes), ADR-0003 (tenant isolation), ADR-0005 (Aspire),
ADR-0006 (exercise sources), ADR-0007 (Lens trust model), ADR-0011 (corpus ingestion),
MVP spec §5.1, §12, §13.9

---

## Context

ADR-0001 was scoped when exercises were hand-authored and the most sensitive column in the system was
`reviews.answer_text`. Two Accepted ADRs moved the ground under it before it was written:

- **ADR-0006:** *"Corpus ingestion is a new trust boundary — the system now reads private source code.
  Every §7 control applies to the corpus, and `BeforeState` is as sensitive as `reviews.answer_text`
  for retention purposes."*
- **ADR-0007 §3:** generation and grading run in **separate processes with separate contexts**, and
  generation's outbound network access is an explicit allowlist.

Neither is a stack decision. Both constrain one: something reads a Git repository, something executes
third-party instructions against it, and this ADR is where "what runs where" is written down.

What was already true in the repository when this was written, and is therefore recorded rather than
chosen: SDK 10.0.400 pinned in `global.json`; Central Package Management with transitive pinning;
the nine projects of §5.1 plus `Whetstone.AppHost` and `Whetstone.ServiceDefaults` (ADR-0005); both
interactive render modes registered with no global `@rendermode` (ADR-0002); `pgvector/pgvector:pg17`
in the AppHost. EF Core and Npgsql are **not yet pinned** — the persistence half of this ADR is
intent that the lockfiles will corroborate in Epic 2.

## Decision

### 1. The baseline

**.NET 10 · Blazor Web App · EF Core 10 + Npgsql · PostgreSQL 17 · self-hosted single-host Docker
Compose.**

The parts that need a reason rather than an assertion:

- **Blazor Web App**, not MVC plus a SPA. One language, one validation implementation, and no
  serialisation boundary through the middle of the practice loop. ADR-0002 carries the render-mode
  half of this argument and is not repeated here.
- **EF Core, not Dapper or raw Npgsql.** §13.9 rejects the repository pattern specifically so EF
  Core's change tracking, query filters and translation behaviour stay visible. Choosing a
  micro-ORM and then hand-rolling tenancy would contradict that: query filters are one of ADR-0003's
  three isolation layers, and they are an EF Core feature.
- **PostgreSQL 17, specifically.** Every one of these is load-bearing somewhere in the design:
  RLS with `FORCE ROW LEVEL SECURITY` (ADR-0003), `FOR UPDATE SKIP LOCKED` (§5.3), BRIN indexes
  (§6.3), and `MAINTAIN` as a grantable privilege (17-only — see `docs/spikes/rls-and-derived-objects.md`).
  "It is the default" would not survive review; these do.
- **pgvector is kept, and it is deliberate.** §12 already says "pre-installed (post-MVP use)"; this
  ratifies it. Provisioning the extension now costs nothing and avoids `CREATE EXTENSION` against a
  live production database later, which is the expensive order. The intended first uses are named so
  it does not stay open-ended: OQ-5's leak-detector similarity threshold, and near-duplicate
  detection over candidates. No §6 table has a vector column in MVP.

### 2. What reads a Git repository

**Clone-and-index — decided in ADR-0011, which closes OQ-9.** The stack consequences that belong
here:

- A per-tenant working clone lives on a volume encrypted at rest, at a path derived from `tenant_id`.
- It is mounted into the generation container **only**. Never into `Whetstone.Web`, never into the
  grading path.
- Fetching is a separate `FetchCorpus` job that holds the read-only, repository-scoped credential.
  Generation does not fetch, and therefore does not hold that credential.

ADR-0003 §D-2 records the isolation asymmetry this creates: the clone has one layer, not three.

### 3. What executes a Lens

**One container per generation run.**

ADR-0007 §3 requires generation isolated from grading. This is the implementation of "separate", and
it reuses §12's existing posture rather than inventing one: non-root, read-only root filesystem,
dropped capabilities, `no-new-privileges`. That gives a real filesystem and network boundary, so
ADR-0007 §3's egress allowlist has something to enforce against and ADR-0007 §7's corpus scope
becomes a path check the adapter can make.

**What this does not buy, stated so nobody assumes otherwise:** a container boundary does not stop a
Lens reading a credential handed to it, or reaching a database it can see. Those are handled
elsewhere and deliberately — the corpus credential is not in the container (§2 above), and the
generation database role is a distinct, narrower role (ADR-0003 §D-6).

`Whetstone.Worker` owns the container lifecycle. Per ADR-0005 this is a Worker concern, not a new
AppHost project.

### 4. §13.9 re-examined, row by row

| Not used | Verdict | Note |
| --- | --- | --- |
| MediatR / mediator library | **Unchanged** | Nothing in ADR-0006/0007 argues for indirection at the call site |
| AutoMapper | **Unchanged** | Hand-written `Select` projections matter more now, not less — the corpus tables are wide |
| Repository pattern over `DbContext` | **Unchanged** | See below |
| Message broker | **Restated, still no** | See below |
| Kubernetes | **Unchanged** | Still one host. Generation containers are spawned by the worker, not scheduled across nodes |
| Microservices | **Unchanged** | See below |

**Repository pattern — unchanged, and worth saying why.** None of the tenancy machinery is a
repository: the connection interceptor is a connection concern, query filters are model
configuration, and handler assertions are application logic. Corpus access is a **port**
(`ICorpusReader`, `IExerciseSource`) declared in `Whetstone.Application` — which is abstracting the
*use case*, exactly what §13.9 says to do instead of wrapping the ORM. The row survives contact with
ADR-0006 and ADR-0007 intact.

**Message broker — the argument is restated, because the original one no longer applies.** §13.9
argued from `GradeSubmission`: short, idempotent, retryable, and §5.3's exponential backoff with max
5 attempts fits that shape. **Generation does not have that shape.** It is a long-running job over an
entire repository, and five retries of a run that dies near the end are five expensive failures, not
resilience.

The answer is still no broker, because the fix is not a broker:

> **A generation run is a first-class row with its own state machine, not an outbox message.**

`generation_runs` carries state, progress and resumability. One outbox message *dispatches* a run;
the run's own row survives it. That gives resumability the outbox cannot, keeps long work from
occupying a dispatcher slot and starving grading, and gives ADR-0007 §5's per-Lens acceptance-rate
metric somewhere to hang.

**What would change the "no broker" answer**, so the next person can check the premise against a
number instead of re-deriving the argument: concurrent tenants × repository size × run duration
exceeding what one host's worker can hold, or a second worker host becoming necessary — at which
point claiming work with `FOR UPDATE SKIP LOCKED` across hosts is still fine, and it is *scheduling
fairness* rather than delivery that would push toward a broker.

**Microservices — unchanged, and the generation container is not one.** It has no independent
deployment cadence, no separate data store, no network partition between it and the rest of the
system, and no independent team. It is a process boundary taken for isolation, which is what
ADR-0007 §3 asked for. Calling it a microservice would be conceding a rule that has not been bent.

### 5. Deliberately used

§13.9 is a "deliberately not used" list. ADR-0006 and ADR-0007 force things *in*, and those deserve
the same one-line defence.

| Used | Why |
| --- | --- |
| A Git access mechanism | ADR-0011. Full clones, local history, no shallow clones (ADR-0008 §2) |
| JSON-schema validation of Lens output | ADR-0007 §4 — invalid output is discarded before it becomes a candidate |
| `HtmlSanitizer` + restricted Markdig pipeline | §7.5, applied to anything a Lens produces that is ever rendered |
| A secret scanner over candidate text | ADR-0007 §4 — a Lens quoting a `.env` file must not create a durable credential copy |
| Container runtime access from the worker | §3 above. The isolation boundary for Lens execution |
| A model-provider SDK | Deferred to OQ-2 (#72), which is still open. Behind `Whetstone.Application`'s AI port (§9.1) either way |

### 6. Contradictions declared

Stated here rather than applied silently:

- **§7.6.2** ("no tools, no retrieval, no function calling in MVP") is superseded by ADR-0007, which
  already names it stale. A Lens is retrieval carrying instructions.
- **§13.9's Aspire paragraph** ("decide before Epic 0.3 and record it as ADR-005 either way") is
  superseded by ADR-0005. Aspire is in for local orchestration.
- **§13.2's `deploy/compose.dev.yml`** is deliberately not created — the AppHost replaced it
  (ADR-0005).
- **§12's container list** gains a fifth entry: generation containers, spawned per run by the worker
  rather than declared in `compose.yml`.

## Consequences

### Good

- One language and one validation implementation across the whole practice loop.
- The isolation ADR-0007 §3 asks for is enforced by a boundary that already has a hardened posture
  in §12, rather than by a convention.
- §13.9 survives the corpus with one row restated and none reopened, which is a stronger position
  than the list has any right to expect after a change this size.

### Bad / costs

- **A durable copy of customer source code now lives inside the deployment.** Bounded by ADR-0011 §1
  and ADR-0003 §D-2, not eliminated.
- The worker needs container runtime access, which is a meaningful privilege for a process that also
  runs the outbox dispatcher. It is the sharpest edge this ADR adds.
- Process count on §12's single host rises: web, worker, migrator, Postgres, Caddy, plus a
  generation container per active run. Disk grows with the sum of tenant repository sizes, which §10
  has no NFR for.
- `deploy/compose.yml` and the `Caddyfile` still do not exist, so §12's production story is
  undemonstrated at the time of writing. Epic 6.

### Neutral

- EF Core and Npgsql pinning lands in Epic 2. Until then this ADR is ahead of the lockfiles, and
  says so.

## Alternatives considered

**Node/TypeScript.** One language across client and server too, and the richest ecosystem for Git
and AI tooling. Rejected: the project's stated purpose (§17) is depth in a stack the author is
responsible for, and EF Core, RLS and Blazor render modes are the specific things being learned.

**ASP.NET Core MVC with htmx.** Genuinely appealing — simpler mental model, no circuits, no
WebAssembly, and the practice loop is mostly form posts. Rejected on the authoring and practice
surfaces, where ADR-0002 wants rich server-validated interactivity, and because it would leave the
render-mode judgment §5.2 exists to exercise untaken.

**A managed platform instead of a self-hosted host.** The strongest alternative. §12's posture —
non-root containers, read-only root filesystems, `ufw`, `fail2ban`, unattended upgrades, monthly
tested restores — is a standing operational cost a managed platform absorbs. Rejected because that
cost *is* part of what is being learned, and because ADR-0011 puts customer source code at rest,
which is a materially different conversation with a third-party platform than with a host you
control. Revisit if the operational load displaces product work.
