# ADR-0011 — Corpus ingestion by clone-and-index

**Status:** Accepted
**Date:** 2026-08-25
**Closes:** OQ-9 (#102)
**Related:** ADR-0006 (exercise sources), ADR-0007 §3 and §7 (Lens trust model), ADR-0008 §2 (drift),
ADR-0001 (stack), ADR-0003 (tenant isolation)

---

## Context

OQ-9 asked whether Whetstone **clones and indexes** a tenant's repository or makes **read-only API
calls per query** against the hosting provider. The trade as ADR-0006 framed it is freshness against
how much private code is at rest.

Three Accepted ADRs were already holding a conditional on the answer:

- **ADR-0007 §3** records that if OQ-9 lands on API-per-query, generation's egress allowlist must
  include the corpus provider, and states that this is *"a weaker guarantee than a single permitted
  endpoint"*, recorded as weaker rather than quietly widened. It names the local-index option as
  keeping the stronger form and says so is *"a point in its favour that belongs in OQ-9's decision."*
- **ADR-0008 §2** requires `DetectDrift` to distinguish "the ref is genuinely gone" from "I could not
  find out", because retirement is one-way. It notes a local index can do this cheaply and that the
  difficulty of doing it over an API *"is a genuine cost of the API-per-query option."*
- **ADR-0005** notes that if isolation lands on a separate deployable, that becomes its own project.

## Decision

**Whetstone clones and indexes. It does not read a tenant's corpus over a provider API at generation
time.**

### 1. What is at rest, where, and for how long

| Material | Where | Retention |
| --- | --- | --- |
| Full working clone of the tenant's repository | Per-tenant directory on a volume encrypted at rest, path derived from `tenant_id` | Life of the corpus registration; deleted by `PurgeDeletedTenant` |
| `ExerciseCandidate.BeforeState` (excerpts) | Postgres, tenant-scoped, all three isolation layers | Candidate retention schedule (ADR-0006) |
| Exercise prompts derived from `BeforeState` | Postgres, tenant-scoped | Life of the Exercise |

The clone is **mounted only into the generation container** (ADR-0001 §3). It is never mounted into
`Whetstone.Web` and never reachable from the grading path.

This is stated plainly because it is the cost of this decision: **the tenant's entire private source
tree is at rest on our disk.** Every §7 control applies to it, and ADR-0003 §D-2 records that the
clone has *one* isolation layer — container mount scope plus path derivation — not three, because
query filters and RLS do not reach a filesystem.

What makes that cost bearable rather than new: ADR-0006 already rules `BeforeState` as sensitive as
`reviews.answer_text`, so encryption at rest, backup scope (#93), the purge path (#87) and the
retention job (#88) are being built regardless. Clone-and-index **widens the blast radius; it does
not create the category.**

### 2. Drift detection reads local history, and absence of evidence stays inconclusive

ADR-0008 §2's rule is unchanged and becomes cheap to honour: `DetectDrift` resolves a `SourceRef`
against the local clone, where the full history is present.

The inconclusive cases are now a short and checkable list:

- The clone is missing, unreadable, or its last fetch failed → **inconclusive.** Retry; change nothing.
- `git fetch` failed for any reason → **inconclusive.**
- The clone is present and current, and the ref does not resolve → **genuine drift.** Retire.

A shallow clone is **not used**, precisely because ADR-0008 §2 names missing history as an
inconclusive case, and a mode whose normal operation produces inconclusive results defeats the rule.
Clones are full.

### 3. Egress keeps the stronger allowlist form

Because generation reads the corpus locally, **the corpus provider is not in generation's egress
allowlist.** ADR-0007 §3's allowlist holds the model provider and nothing else, denied by default —
the stronger form that ADR-0007 hoped for.

Fetching is therefore **not** a generation-time act. A separate `FetchCorpus` job, outside the
generation container and outside the Lens execution context, updates the clone. Lens code never
initiates network traffic to the corpus provider, because by the time it runs there is nothing to
fetch.

### 4. The credential lives with the fetcher, not with generation

The corpus credential is held by the `FetchCorpus` job only. It is not present in the generation
container's environment, so a malicious Lens cannot read it out (ADR-0001 §3 records that a
container boundary does not otherwise stop a Lens reading what it is handed).

Scope: **read-only, repository-scoped**, one credential per corpus registration. It is the
highest-value secret in the system and is subject to §7.8.

### 5. Corpus scope is enforced at the adapter, over the local tree

ADR-0007 §7 requires a Lens's declared corpus scope to be enforced at the adapter rather than by
asking the Lens to behave. Over a local clone this is a path check on every read, which is cheaper
and more complete than the API equivalent — there is no second code path where a Lens could reach
the provider directly, because it has no credential and no route.

## Consequences

### Good

- ADR-0008 §2's one-way-retirement safety becomes cheap and obviously correct.
- ADR-0007 §3's allowlist keeps its strong form; one fewer permitted endpoint in the process that
  runs third-party instructions.
- Arbitrary traversal — `git log`, blame, full-tree walks — is local, fast and free of rate limits,
  which is what an `ExerciseSource` of kind `GitHistory` actually needs.
- No provider coupling in the generation path; a second corpus provider is a second fetcher, not a
  second generation implementation.

### Bad / costs

- **The full private tree is at rest.** This is the decision's real price and it is not mitigated
  away, only bounded: encrypted volume, single mount, per-tenant path, deleted on purge.
- Freshness is whatever the last fetch was. Exercise generation can run against a tree that is hours
  old, and drift detection can report a retirement one fetch late.
- Disk grows with the sum of tenant repository sizes, which is a capacity dimension §10 has no NFR
  for.
- Backup scope (#93) now includes source code, which changes what an off-host backup is. ADR-0003
  §D-7 states the residual window rather than claiming instant deletion.

### Neutral

- `FetchCorpus` is a fourth job alongside §5.3's four, and it is the only one holding the corpus
  credential.

## Alternatives considered

**Read-only API per query.** Least code at rest, always current. Rejected on ADR-0008 §2: it
requires correctly classifying 404 vs 403 vs 5xx vs timeout on every resolution, and getting that
wrong permanently retires valid exercises — one expired token could empty a tenant's exercise set
overnight, irreversibly. It also forces the corpus provider into the egress allowlist of the process
running third-party instructions, which ADR-0007 §3 already records as the weaker guarantee.

**Hybrid: shallow clone plus API for freshness.** Rejected — it carries both failure modes and both
credentials, and ADR-0008 §2 names a shallow clone missing history as inconclusive, so the drift
problem returns anyway.

**Ephemeral clone per run, discarded afterwards.** The strongest privacy answer: nothing durable at
rest beyond `BeforeState`. Rejected for MVP because it gives back the offline, cheap-traversal
property that motivated clone-and-index, and turns every drift run into a full re-clone. **Worth
revisiting** if corpus-at-rest becomes the binding compliance constraint under §7.9 — it is the
option that trades cost for exposure most directly.

**Repository contents in Postgres.** Would put the corpus behind RLS like everything else. Rejected —
it is building a filesystem in a database; git operations stop being git operations, and WAL and
backup volume grow by the size of every tenant's tree.
