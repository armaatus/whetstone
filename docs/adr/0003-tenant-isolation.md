# ADR-0003 — Tenant isolation

**Status:** Draft — scaffold. No decision in this file has been taken.
**Date:** — (set when accepted)
**Related:** ADR-0006 (exercise sources), ADR-0007 (Lens trust model), MVP spec §7.3, §6.2, §6.4,
`CONTEXT.md` ("Tenant"), `docs/spikes/rls-and-derived-objects.md`

---

> **This is a scaffold, not an ADR.** Tenancy, RLS and the connection interceptor are on the §17
> not-AI-assisted list, and this file honours that: it contains **no decision**. What it contains is
> the surface to cover, the questions each layer has to answer against that surface, and the failure
> modes to price.
>
> When taking the ADR: answer each `D-n`, fill the coverage matrix, delete this block, set
> **Status: Accepted** with a date.

---

## Context

### The spec's position (§7.3), unchanged

> Three independent layers. Any one failing must not cause a breach.
>
> **Layer 1** — EF Core global query filters, applied by convention so a new entity cannot be
> forgotten.
> **Layer 2** — Postgres RLS with `FORCE ROW LEVEL SECURITY`, three roles
> (`whetstone_migrator`, `whetstone_app` without `BYPASSRLS`, `whetstone_readonly`), and a
> `DbConnectionInterceptor` that sets `app.tenant_id` on open and **resets it on close**.
> **Layer 3** — application assertions: every command handler asserts the loaded aggregate's
> `TenantId` equals the ambient tenant.
>
> Tenant resolution is from the authenticated user's claim only.

`CONTEXT.md` states the boundary in its widest form:

> **Tenant** — an organisation. The isolation boundary for **all data, all corpora, and all
> candidates.**

### Why this ADR is being written against a wider surface than §7.3 assumed

§7.3 was written when the tenant-scoped surface was §6.2's twelve tables and the most sensitive
column was `reviews.answer_text`. ADR-0006 and ADR-0007 added three things that §7.3 never
considered:

1. **Corpora** — verbatim private source code, which ADR-0006 rules is *as sensitive as*
   `reviews.answer_text` for retention purposes.
2. **`ExerciseCandidate`s** — holding `BeforeState` (source code), plus `Finding` and `Rationale`,
   which carry a second constraint on top of tenancy: they may be persisted but must never be
   serialised into a grading prompt.
3. **The Lens registry** — which is *not* uniformly tenant-scoped, and is the only part of the
   system where that is true.

A tenancy ADR scoped to the old surface is not merely incomplete. It is the stale security premise
ADR-0007 names explicitly: *"a stale security premise is worse than none."*

### The hole found in Epic 5 realignment (#83)

**Derived database objects do not inherit RLS.** A materialised view over a tenant-scoped table
carries no policy of its own — layer 2 is simply *absent*, while layers 1 and 3 silently carry its
weight. §7.3's "any one failing must not cause a breach" is false for such an object unless it is
handled deliberately. §6.4's `mv_team_mastery_daily` is the instance that surfaced this; the rule
owed here is the general one.

**This has since been measured** — `docs/spikes/rls-and-derived-objects.md`, against PostgreSQL 17.10
and EF Core 10. The short version: layer 1 *is* available (keyless entity types do carry query
filters), and layer 2 is not merely absent for a materialised view — the view holds whichever
tenant's rows were visible to the session that last refreshed it, and serves them to every tenant.
D-4 below carries the full result and what it does to the option list.

---

## Coverage matrix — fill this in

Every tenant-scoped object × three layers. Blank cells are the work.

| Object | L1 query filter | L2 RLS policy + FORCE | L3 handler assertion |
| --- | --- | --- | --- |
| §6.2 core tables (`cards`, `reviews`, `sessions`, `exercises`, `exercise_versions`, `skills`, `skill_prerequisites`, `mastery_snapshots`, `outbox_messages`, `audit_events`, `ai_usage`, `users`) | | | |
| `tenants` (the row *is* the tenant — self-referential; state the rule) | | | |
| corpora (metadata; and the clone/working copy if ADR-0001 D-2 lands on one) | | | |
| `exercise_candidates` (incl. `BeforeState`, `Finding`, `Rationale`) | | | |
| generation runs (ADR-0007 §3 — per-tenant, per-Lens, per-scope) | | | |
| Lens **identity** + pinned content hash (platform-level? see D-3) | | | |
| Lens **enablement** + granted corpus scope (per tenant — see D-3) | | | |
| Lens audit events (`audit_event` per ADR-0007 §2) | | | |
| `mv_team_mastery_daily` and every other derived object (see D-4) | | | |

`skill_prerequisites` is worth a deliberate look: §6.2 gives it a composite PK of two skill ids and
**no `tenant_id` column**. Either it gains one or layer 1 and layer 2 have nothing to filter on, and
isolation for the skill DAG rests entirely on layer 3.

---

## Decision

### D-1 — The three layers, restated against the full surface

Not a copy of §7.3. The question is what each layer means for objects §7.3 never saw: a table whose
sensitive column is source code, a table that is deliberately *not* tenant-scoped, and an object that
is not a table at all.

### D-2 — Corpora and candidates

- Which columns are the sensitive ones, and does anything beyond tenancy apply to them?
  `Finding`/`Rationale` carry a non-tenancy invariant (never into a grading prompt) — say whether
  that is enforced anywhere near this ADR's machinery or entirely elsewhere, so the reader is not
  left assuming RLS covers it.
- If ADR-0001 D-2 lands on a clone, **the clone is outside the database entirely.** Layers 1 and 2
  do not reach a filesystem. State what does — path scoping per tenant, container boundary, disk
  encryption — or state that this object has one layer and record that as a known asymmetry rather
  than letting the "three layers" claim quietly cover it.

### D-3 — How the Lens registry splits across the tenant boundary

**This is the decision the ticket asks for explicitly, rather than leaving to the schema.** ADR-0007
§2 and §7 give the shape: a Lens is pinned `id@version` + content hash; no Lens reads a corpus until
a `TenantAdmin` enables it for that tenant; enablement records who, when, at which hash, and what
corpus scope; every enable/disable/upgrade writes an `audit_event`; a new version of an already
enabled Lens is disabled by default.

Which side of the boundary does each of these sit on?

| Thing | Platform or tenant | Consequences to state |
| --- | --- | --- |
| Lens id, version, content hash | ? | If platform-level: no `tenant_id`, so **layer 1 and layer 2 have nothing to filter on** |
| Registration metadata (who registered it upstream, tier) | ? | |
| Enablement (tenant, lens, hash, enabled-by, enabled-at) | ? | ADR-0007 §2 makes this a `TenantAdmin` act — that reads as tenant-scoped, so say so |
| Granted corpus scope | ? | ADR-0007 §7 enforces it "at the adapter" — is that layer 3, or a fourth thing? |
| Lens audit events | ? | `audit_events` already carries `tenant_id` (§6.2) |

Two questions that fall out and should not be left implicit:

1. §7.3 applies query filters **by convention so a new entity cannot be forgotten.** A
   platform-level table has to *opt out* of that convention. How is the opt-out made deliberate and
   testable, so "no filter" never becomes indistinguishable from "filter forgotten"? An allow-list
   the architecture tests assert against is the obvious shape; whatever it is, name it here.
2. Can tenant A learn what tenant B enabled by reading the platform-level table? Content hashes and
   version numbers of publicly-authored skills are not secret, but *the set a tenant enabled* is a
   signal about their codebase. Decide whether the platform table is readable by `whetstone_app` at
   all, and whether enablement is joinable across tenants.

### D-4 — The rule for derived objects

**Answered by spike, `docs/spikes/rls-and-derived-objects.md` (PG 17.10, EF Core 10).** The two
open questions from #83 are now closed as *facts*; what remains open is which option to take.

**Fact 1 — layer 1 is available.** Keyless entity types do carry EF Core global query filters,
including EF 10 named filters and selective `IgnoreQueryFilters([...])`, and they appear in
`Model.GetEntityTypes()` so a convention sweep reaches them. The gap is **one layer of three, not
two.**

**Fact 2 — layer 2 is unavailable, and its absence inverts rather than merely gaps.**
`CREATE MATERIALIZED VIEW` has no `security_invoker`/`security_barrier` option and
`ALTER MATERIALIZED VIEW` has no `ENABLE`/`FORCE ROW LEVEL SECURITY` subform, so there is no policy
to attach. Worse, a matview is a **frozen snapshot taken under whatever tenant context ran the
refresh**: in the spike, reading as the app role under tenant B returned **tenant A's row**. The
object does not hold "unfiltered data" — it holds *one arbitrary tenant's* data and serves it to
everyone.

Two consequences that constrain the options below:

- With `FORCE` on the base table and no tenant set, the refresh **errors**
  (`unrecognized configuration parameter "app.tenant_id"`) rather than returning nothing. A matview
  over a FORCE-RLS table cannot be built tenant-neutrally unless the policy uses the missing-ok
  `current_setting('app.tenant_id', true)` form or the refresh runs under a role that bypasses RLS.
- `REFRESH` is denied to the app role outright; PG 17's `MAINTAIN` privilege grants it without
  ownership, and the refresh then runs under the **invoker's** RLS context. §6.4 has the worker
  refreshing this nightly, so whichever role it uses, one tenant's data ends up in a global object.

**So a correct cross-tenant rollup matview must be refreshed by a role that bypasses RLS** — which
reintroduces precisely the bypass path §7.3 exists to remove. That is the real cost of option 3
below, and it was not visible before the spike.

Options, with the spike's costs attached:

| Option | Buys | Costs |
| --- | --- | --- |
| **Rollup table** written by the worker, carrying `ENABLE` + `FORCE ROW LEVEL SECURITY` | It is a table, so all three layers apply normally. No bypass role | The worker owns refresh semantics that `REFRESH ... CONCURRENTLY` gave free, and must loop per tenant setting `app.tenant_id`. Loses `CONCURRENTLY`'s non-blocking swap |
| **Ordinary view** with `security_invoker = true` | Verified safe in the spike, with and without `FORCE`. Nothing new to operate | Not materialised, so it does not solve the NFR-2 problem that made §6.4 reach for a matview in the first place |
| **Matview refreshed by a bypassing role**, wrapped in a `security_barrier` + `security_invoker` view carrying an explicit tenant predicate | Keeps matview performance | The predicate is layer 1 re-implemented in SQL — a *fourth* thing, not layer 2. Needs an audited bypass role. `security_barrier` is required or a leaky operator reads around the predicate |
| **Per-tenant views** | Isolation by construction | An object per tenant, and DDL at tenant-creation time, which the app has no rights to do (§6.5) |
| **Accept the gap** with a compensating test and written justification | Cheapest; honest if written down | §7.3's "any one failing must not cause a breach" is false for these objects. Given fact 2 this is not "one layer down" but "actively serves the wrong tenant", so the justification has to carry more weight than it did before the spike |

**Also record, from §4 of the spike:** `FORCE ROW LEVEL SECURITY` turns out to be load-bearing for
*ordinary* views too — with `FORCE` dropped, the owner-rights view leaked both tenants. §7.3 mandates
`FORCE` for the owner-bypass reason; this is a second, independent reason for the same line, and it
is the one that covers views. It is worth stating, because "we have FORCE because of owner-bypass"
invites someone who solved owner-bypass another way to drop it.

And note *why* the owner-rights view was safe: §7.3's policy compares a **session variable**, which
is set in the invoker's session whoever's rights evaluate the view. A policy written against
`current_user` would not survive an owner-rights view. The safety is a property of the policy's
shape, not of views.

Whatever is chosen, the rule needs a home that survives the next derived object: a convention plus a
test that fails when one exists without a covering decision. Otherwise this gets rediscovered once
per view, and the third time nobody notices.

### D-5 — Tenant resolution, including where there is no user

§7.3 says: from the authenticated user's claim only, never from a header, query string, subdomain or
route parameter. That holds for requests. **The worker has no user**, and three of §5.3's four jobs
plus every generation run execute there.

- `outbox_messages` carries `tenant_id` (§6.2), so the dispatcher can set `app.tenant_id` from the
  message. State that as the rule, and state the failure mode: a message with a null or wrong
  `tenant_id` is a cross-tenant write with every layer satisfied.
- `SnapshotMastery` (daily, all tenants) and `PurgeDeletedTenant` are inherently cross-tenant. Do
  they loop per tenant with the tenant set each iteration, or run under a role that bypasses RLS?
  If anything bypasses, name the role, name what may run under it, and say how that is audited.
  An unaudited `BYPASSRLS` path is the whole defence undone by one convenience.

### D-6 — The pooling footgun, extended to the generation process

§7.3 calls the interceptor's reset the single most dangerous line in the system, and SEC-2 (200
concurrent requests, 10 tenants, 5 connections, zero cross-tenant rows) is the test. ADR-0007 §3's
generation process opens *its own* connections, outside the web request pipeline.

- Which role does it connect as? Does it set `app.tenant_id`, through the same interceptor or a
  different path?
- **Is SEC-2 extended to cover it, or does a second test exist?** A test that only exercises the web
  pool is a test of half the connections.
- If generation and grading share a pool at any point, that contradicts ADR-0007 §3 — say so here
  rather than resolving it silently.

### D-7 — Deletion and retention reach every object above

ADR-0006 puts `BeforeState` at the same sensitivity as `reviews.answer_text`. `PurgeDeletedTenant`
therefore has to reach corpora metadata, candidates, generation runs, any clone on disk, every
derived object, and the tenant's enablement rows — while leaving platform-level Lens identity alone.
List the objects it must touch, and say how that list is kept honest as new tables land.

---

## Tests this ADR should name

The spec's convention is that a security rule ships with a test id. Candidates:

- **SEC-2**, extended per D-6.
- A test that fails when a tenant-scoped entity exists with no RLS policy — the layer-2 mirror of
  §7.3's by-convention query filter, and the thing that makes "cannot be forgotten" true rather than
  aspirational.
- A test asserting every derived object is covered by D-4's rule.
- A test for D-3's opt-out allow-list: a new entity without `tenant_id` fails unless it is listed.
- A cross-tenant read attempt per new object in the coverage matrix, not one generic case.

## Consequences

*To write.* Good / Bad-and-costs / Neutral, per the house format.

## Alternatives considered

*To write.* Worth recording honestly: **schema-per-tenant** and **database-per-tenant** (both trade
migration and connection complexity for isolation that needs no interceptor), and **application-layer
filtering only** — which is what most systems actually ship and which OQ-6 is quietly adjacent to,
since the answer there may be "one tenant for a long while".
