# ADR-0003 — Tenant isolation

**Status:** Accepted
**Date:** 2026-08-25
**Related:** ADR-0001 (stack), ADR-0006 (exercise sources), ADR-0007 (Lens trust model),
ADR-0011 (corpus ingestion), MVP spec §7.3, §6.2, §6.4, `CONTEXT.md` ("Tenant"),
`docs/spikes/rls-and-derived-objects.md`

---

## Context

§7.3 fixes the shape: three independent layers, any one failing must not cause a breach. EF Core
global query filters applied by convention; Postgres RLS with `FORCE ROW LEVEL SECURITY` and three
roles; application assertions in every command handler. Tenant resolution from the authenticated
user's claim only.

`CONTEXT.md` states the boundary in its widest form: **Tenant — an organisation. The isolation
boundary for all data, all corpora, and all candidates.**

§7.3 was written when the tenant-scoped surface was §6.2's tables and the most sensitive column was
`reviews.answer_text`. ADR-0006, ADR-0007 and ADR-0011 added three things it never considered:
**corpora** (now including a full working clone at rest), **`ExerciseCandidate`s** holding verbatim
source in `BeforeState`, and **the Lens registry**, which is the only part of the system that is not
uniformly tenant-scoped.

A tenancy ADR scoped to the old surface would be the stale security premise ADR-0007 names
explicitly: *"a stale security premise is worse than none."*

`docs/spikes/rls-and-derived-objects.md` measured the derived-objects question raised in #83 against
PostgreSQL 17.10 and EF Core 10. Its results are load-bearing below and are not re-argued here.

## Decision

### 1. The three layers stand, and the surface they cover is enumerated

| Object | L1 query filter | L2 RLS + FORCE | L3 handler assertion |
| --- | --- | --- | --- |
| §6.2 core tables | ✔ by convention | ✔ | ✔ |
| `tenants` | n/a — the row *is* the tenant; reads are by id from the claim | policy on `id` | ✔ |
| `corpora` (registration metadata) | ✔ | ✔ | ✔ |
| The working **clone** on disk | — | — | see §2 |
| `exercise_candidates` | ✔ | ✔ | ✔ |
| `generation_runs` | ✔ | ✔ | ✔ |
| Lens **version** (id, version, hash, tier) | opted out, explicitly — see §3 | none — platform-level | n/a |
| Lens **enablement** (+ corpus scope) | ✔ | ✔ | ✔ |
| Lens audit rows (`audit_events`) | ✔ | ✔ | ✔ |
| Derived objects | ✔ | ✔ — see §4 | ✔ |
| `skill_prerequisites` | ✔ — see §7 | ✔ | ✔ |

### 2. The clone has one layer, and that is recorded rather than glossed

ADR-0011 puts a full working clone of each tenant's repository on disk. **Query filters and RLS do
not reach a filesystem.** What guards it:

- A per-tenant directory on a volume encrypted at rest, at a path derived from `tenant_id`.
- Mounted into the generation container only (ADR-0001 §3) — never into `Whetstone.Web`, never into
  the grading path.
- Deleted by `PurgeDeletedTenant` (§6).

**This is one layer, not three, and §7.3's "any one failing must not cause a breach" does not hold
for it.** That is stated here deliberately. The failure this avoids is not the asymmetry itself — it
is letting the phrase "three layers" appear to cover an object it structurally cannot.

### 3. The Lens registry splits, and each side is named

ADR-0007 uses "registration" for two different acts. This ADR separates them.

**`lens_versions` — platform-level, no `tenant_id`.** Holds `lens_id`, `version`, `content_hash`,
`tier`. These are facts about a publicly-authored artefact, identical for every tenant. A single
source of truth is the point: hash-mismatch detection (ADR-0007 §1) fails loudly against one pinned
hash, not against one hash per tenant.

**`lens_enablements` — tenant-scoped, all three layers.** Holds `tenant_id`, `lens_version_id`,
`enabled_by`, `enabled_at`, `corpus_scope`. Enablement is a `TenantAdmin` act (ADR-0007 §2) and
every enable/disable/upgrade writes an `audit_event`, which already carries `tenant_id`.

**Enumeration is not a concern here, and the reason is structural.** `lens_versions` contains
nothing tenant-specific — the sensitive fact is *which* Lenses a tenant enabled, and that lives
entirely on the tenant side under RLS. Reading `lens_versions` reveals only public skill metadata.

**The opt-out is explicit and tested.** §7.3 applies query filters by convention so a new entity
cannot be forgotten; a platform-level table must opt *out* of that convention. The opt-out is an
allow-list asserted by an architecture test: a tenant-scoped-by-default entity with no `tenant_id`
fails the build unless it is named on the list. "No filter" must never become indistinguishable from
"filter forgotten".

### 4. Derived objects: rollup tables, not materialised views

**Rule: any object that aggregates tenant-scoped data and is queried directly is a table carrying
`ENABLE` + `FORCE ROW LEVEL SECURITY`, written per tenant by the worker.** §6.4's
`mv_team_mastery_daily` becomes `team_mastery_daily`.

The spike is why. A materialised view takes no policy — no `security_invoker` on `CREATE`, no
`ENABLE`/`FORCE ROW LEVEL SECURITY` on `ALTER` — and it is a frozen snapshot taken under whatever
tenant context ran the refresh. Reading as the app role under tenant B returned **tenant A's row**.
The object does not hold unfiltered data; it holds one arbitrary tenant's data and serves it to
everyone.

And a *correct* cross-tenant rollup matview would have to be refreshed by a role that bypasses RLS —
reintroducing precisely the path §7.3 exists to remove. That cost is what decides this.

Costs accepted: the worker owns refresh semantics that `REFRESH MATERIALIZED VIEW CONCURRENTLY` gave
for free, and the non-blocking swap is lost. The per-tenant write loop is not an extra cost — §5
already requires per-tenant execution for every background job.

**Two findings from the spike that belong in this document rather than only in the spike:**

- **`FORCE ROW LEVEL SECURITY` is load-bearing for ordinary views, not only for direct owner access.**
  With `FORCE` dropped, an owner-rights view leaked both tenants. §7.3 mandates `FORCE` because a
  table owner otherwise bypasses its own policies; this is a **second, independent reason** for the
  same line. Recorded because "we have `FORCE` because of owner-bypass" invites someone who has
  solved owner-bypass another way to drop it.
- **The safety of an owner-rights view is a property of the policy's shape.** §7.3's policy compares
  a *session variable*, which is set in the invoker's session whoever's rights evaluate the view. A
  policy written against `current_user` would not survive an owner-rights view. Policies stay
  session-variable-shaped.

A convention plus a test enforces the rule for objects that do not exist yet: a derived object
without a covering decision fails the build. Otherwise this gets rediscovered once per view, and the
third time nobody notices.

### 5. The worker has no user, so no job runs cross-tenant

§7.3 resolves tenant from the authenticated claim. That holds for requests and says nothing about
the worker, where `SnapshotMastery`, `PurgeDeletedTenant`, `FetchCorpus` and every generation run
execute.

- The dispatcher sets `app.tenant_id` from `outbox_messages.tenant_id`.
- **Work that is conceptually cross-tenant is decomposed by a scheduler into one message per
  tenant.** `SnapshotMastery` becomes N messages, not one loop. No job ever executes cross-tenant,
  so **there is no bypass role to audit** — the strongest form of "audited bypass" is not having one.
- The single carved-out cross-tenant read is the scheduler enumerating `tenants` to fan out. It is
  named, restricted to the scheduler, and audited.

**Failure mode, stated because all three layers are satisfied when it happens:** a message with a
null or wrong `tenant_id` is a cross-tenant write that nothing catches. `outbox_messages.tenant_id`
is therefore `NOT NULL` and set by the `SaveChanges` interceptor from the ambient tenant, never
passed by hand at a call site.

This also avoids the pooling footgun by construction: a per-tenant message holds a connection for
one tenant context, where an in-process loop over tenants would carry many contexts across one
connection — §7.3's "single most dangerous line".

### 6. Generation gets its own role and its own pool

ADR-0007 §3 says generation may not read user answers. That is enforced by a **grant**, not by the
code being correct.

A fourth role joins §7.3's three: **`whetstone_generation`**, with access to `corpora`,
`exercise_candidates` and `generation_runs`, and **no grant at all** on `reviews`, `cards` or
`sessions`. Separate connection pool, same interceptor, same `app.tenant_id` discipline.

Tests: **SEC-2 is extended to cover the generation pool** — a test that only exercises the web pool
tests half the connections — and a second test asserts the generation role is denied on the grading
tables.

### 7. `skill_prerequisites` gains a tenant, structurally

§6.2 gives it a composite PK of two skill ids and no `tenant_id`, so layers 1 and 2 have nothing to
bind to and the skill DAG rests on layer 3 alone — the one table where §7.3's claim is false by
construction.

Fix, while the table is empty: add `tenant_id`, add a unique index on `skills (tenant_id, id)`, and
make **both** edge ends composite foreign keys to `(tenant_id, id)`. A cross-tenant edge becomes
unrepresentable — the database refuses the write — rather than merely filtered out of reads. One
index and one migration, now, instead of a data migration later.

### 8. Deletion reaches everything, and the backup window is a number

`PurgeDeletedTenant` deletes, in order: candidates and generation runs, core §6.2 rows, rollup rows
(§4), enablement rows (§3) — **not** `lens_versions`, which is platform-level — and last, with
verification, the working clone directory (§2).

**Backups are the part that is usually implied and should not be.** NFR-7 ships nightly encrypted
`pg_dump`s off-host, and ADR-0011 puts source code in scope for them (#93). Purged data persists in
backups until the rotation ages it out. **This ADR states that window explicitly as a number rather
than implying deletion is instant** — §7.9 makes it a question you will be asked, and "we delete
immediately" is not true of any system with backups.

Per-tenant encryption keys with crypto-shredding would make backup copies unreadable at purge time
and genuinely solve this. Deferred: it adds key management, key rotation, and a new way to lose a
tenant's data irrecoverably. Revisit if §7.9 makes the documented window unacceptable.

## Tests named by this ADR

- **SEC-2**, extended to the generation pool (§6).
- The generation role is denied on `reviews`, `cards`, `sessions` (§6).
- A tenant-scoped entity with no RLS policy fails the build — the layer-2 mirror of §7.3's
  by-convention query filter, and what makes "cannot be forgotten" true rather than aspirational.
- An entity without `tenant_id` fails the build unless it is on §3's allow-list.
- A derived object without a covering §4 decision fails the build.
- A cross-tenant read attempt per object in §1's table, not one generic case.
- A cross-tenant `skill_prerequisites` insert is rejected by the database (§7).

## Consequences

### Good

- No `BYPASSRLS` role exists in production (§5). The most dangerous escape hatch is absent rather
  than audited.
- ADR-0007 §3's "generation may not read user answers" becomes a database grant instead of a code
  review (§6).
- Cross-tenant skill edges become unrepresentable rather than filtered (§7).
- The two places where the three-layer claim genuinely does not hold — the clone, and platform-level
  `lens_versions` — are named in the document instead of discovered later.

### Bad / costs

- The worker owns rollup refresh semantics and loses `REFRESH ... CONCURRENTLY` (§4).
- A fourth database role to provision, grant and keep in sync with migrations (§6).
- The scheduler fan-out is more moving parts than a loop, and the `tenants` enumeration is a real
  carve-out even though it is narrow (§5).
- The clone is protected by one layer and by container configuration, which is a weaker guarantee
  than everything else in this document (§2).
- Purged data survives in backups for a stated window (§8).

### Neutral

- §6.4's `mv_team_mastery_daily` is renamed and re-typed. It is mapped as a normal entity rather than
  a keyless one, so the keyless-entity question the spike answered turns out not to arise here — the
  answer is kept in the spike because the next derived object may not be a table.

## Alternatives considered

**Schema-per-tenant / database-per-tenant.** Isolation with no interceptor and no policy — the
strongest form, and the one that makes the pooling footgun disappear. Rejected: migrations become
N migrations, connection pooling becomes per-tenant, and the cross-tenant team rollup gets harder
rather than easier. Also, §17 makes multi-tenancy the security exercise; the interceptor and RLS are
the parts worth having built.

**Application-layer filtering only.** What most systems ship. Rejected — it is layer 3 alone, which
§7.3 already rejects, and OQ-6 ("is one tenant enough for MVP?") is not a reason to build it weakly:
a system with one tenant and correct isolation is a system that can take a second one.

**Matview plus an audited bypass role plus a `security_barrier` wrapper view.** Keeps matview
performance. Rejected in §4 — the tenant predicate in the wrapper is layer 1 re-implemented in SQL, a
fourth thing rather than a restored layer 2, and it requires the bypass role §5 avoids.

**`security_invoker` view without materialisation.** Verified safe in the spike, with and without
`FORCE`. Rejected only because it abandons materialisation, which is what §6.4 reached for to meet
NFR-2. If §6.4 step 1's measurement shows indexes alone clear p95 < 500 ms, this becomes the simpler
answer and §4's rule should be revisited.
