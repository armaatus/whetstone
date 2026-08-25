# Spike — RLS and derived objects

**Date:** 2026-08-25
**Question:** ADR-0003 §4 — do derived database objects carry tenant isolation, and does a keyless
entity type carry an EF Core global query filter?
**Verdict:** Layer 1 is available on derived objects. **Layer 2 is not, for materialised views —
and its absence is worse than a gap: it inverts.**

Run against PostgreSQL 17.10 and EF Core 10 (`Microsoft.EntityFrameworkCore.Sqlite` 10.x for the
model-level checks, which are provider-independent). Everything below was executed, not reasoned
about; the reproductions are at the bottom.

---

## 1. EF Core — keyless entity types **do** carry query filters

Layer 1 is available on `mv_team_mastery_daily`. Confirmed against EF Core 10:

- `HasQueryFilter` on a `HasNoKey()` + `ToView(...)` type builds and emits the predicate:
  `SELECT ... FROM "mv_team_mastery_daily" AS "m" WHERE "m"."TenantId" = @ef_filter__tenantId`.
- EF 10 **named** filters work on keyless types, including selective disable:
  `IgnoreQueryFilters(["Soft"])` drops only that one and leaves the tenant filter in place.
- Keyless types **do** appear in `Model.GetEntityTypes()`, so a convention sweep in
  `OnModelCreating` reaches them.

One caveat that matters for §7.3's "applied by convention so a new entity cannot be forgotten":
the docs say keyless entity types "are never discovered by convention". That is about EF not
*adding the type to the model* by itself — it must be registered explicitly with `[Keyless]` or
`.HasNoKey()`. Once registered, a sweep over `GetEntityTypes()` sees it like any other. So the
by-convention guarantee holds, and the failure mode is a type nobody registered, not a registered
type the sweep skipped.

## 2. Postgres — materialised views cannot carry a policy

From the PG 17 grammar, not inference:

- `CREATE MATERIALIZED VIEW` accepts `IF NOT EXISTS`, `USING`, `WITH (storage_parameter)`,
  `TABLESPACE`, `AS query`, `WITH [NO] DATA`. **No `security_invoker`, no `security_barrier`.**
- `ALTER MATERIALIZED VIEW`'s subforms are a subset of `ALTER TABLE`'s, and
  **`ENABLE`/`FORCE ROW LEVEL SECURITY` are not among them.**

So there is no policy to attach and no invoker-rights escape. Layer 2 is not weak here; it is
unavailable.

## 3. The part that is worse than a gap

A materialised view is a **frozen snapshot taken under whatever tenant context ran the refresh.**

Setup: `cards` with `ENABLE` + `FORCE ROW LEVEL SECURITY` and §7.3's policy verbatim
(`USING (tenant_id = current_setting('app.tenant_id')::uuid)`), owned by `migrator`; two rows, one
per tenant; `app` is a non-owner role without `BYPASSRLS`.

| Read as `app`, `app.tenant_id` = **tenant B** | Result |
| --- | --- |
| `cards` (base table) | tenant B only ✔ |
| `v_invoker` (regular view, `security_invoker=true`) | tenant B only ✔ |
| `v_default` (regular view, PG default owner-rights) | tenant B only ✔ — but see §4 |
| `mv_mastery` (materialised view) | **tenant A's row. Cross-tenant read.** ✘ |

The matview had been built in a session where `app.tenant_id` was tenant A. Every tenant then reads
tenant A's data out of it. #83 called this "isolation layer 2 is simply absent"; it is stronger than
that — the object holds *one arbitrary tenant's rows* and serves them to everyone.

Two further results from the same run:

- **With `FORCE` on and no tenant set, the refresh does not silently produce nothing — it errors:**
  `ERROR: unrecognized configuration parameter "app.tenant_id"`. So a matview over a FORCE-RLS table
  cannot be built or refreshed tenant-neutrally at all, unless the policy uses the missing-ok form
  `current_setting('app.tenant_id', true)` or the refresh runs as a role that bypasses RLS.
- **`REFRESH` needs privilege the app role does not have.** As `app`:
  `ERROR: permission denied for materialized view mv_mastery`. PG 17's `MAINTAIN` privilege
  (`GRANT MAINTAIN ON mv_mastery TO app`) grants it without making `app` the owner — and the refresh
  then runs under **the invoker's** RLS context, baking tenant B's rows in. §6.4 says the worker
  refreshes this nightly; whichever role it uses, the result is one tenant's data in a global object.

The consequence for the option list: a *correct* cross-tenant rollup matview has to be refreshed by
a role that bypasses RLS, which reintroduces exactly the bypass path §7.3 exists to remove.

## 4. `FORCE ROW LEVEL SECURITY` is load-bearing for ordinary views too

`v_default` above is an ordinary owner-rights view, and it was safe. It is safe **only because of
`FORCE`**. Dropping `FORCE` and re-reading as `app` with tenant B set:

```
v_default (owner rights) with FORCE removed:
 11111111-1111-1111-1111-111111111111 | 1
 22222222-2222-2222-2222-222222222222 | 1      <- both tenants
```

`v_invoker` stayed correct in both runs.

§7.3 already mandates `FORCE`, for the reason that a table owner otherwise bypasses its own policies.
This is a **second, independent reason** for the same line, and it is the one that covers views. Worth
recording, because "we have FORCE because of the owner-bypass problem" invites someone who has solved
the owner-bypass problem another way to drop it.

Note also *why* `v_default` works: the policy compares against a **session variable**, which is set
in the invoker's session regardless of whose rights evaluate the view. A policy written against
`current_user` instead would not survive an owner-rights view. The safety comes from the shape of
§7.3's policy, not from views being safe in general.

## 5. Reproductions

EF Core probe — `net10.0` console, `Microsoft.EntityFrameworkCore.Sqlite` 10.x:

```csharp
b.Entity<TeamMasteryDaily>(e =>
{
    e.HasNoKey();
    e.ToView("mv_team_mastery_daily");
    e.HasQueryFilter("Tenant", v => v.TenantId == tenantId);
    e.HasQueryFilter("Soft",   v => !v.IsDeleted);
});
// db.TeamMastery.ToQueryString()                        -> both predicates
// db.TeamMastery.IgnoreQueryFilters(["Soft"])           -> tenant predicate only
```

Beware when probing this: `DbContext` caches the built model per context type, so two contexts of
the same type configured differently in one process silently share the first model.

Postgres probe — `docker run --rm -d -e POSTGRES_PASSWORD=probe -p 55499:5432 postgres:17`:

```sql
CREATE ROLE migrator LOGIN PASSWORD 'x';
CREATE ROLE app      LOGIN PASSWORD 'x';   -- no BYPASSRLS, not the owner
GRANT CREATE, USAGE ON SCHEMA public TO migrator;
GRANT USAGE ON SCHEMA public TO app;

SET ROLE migrator;
CREATE TABLE cards (id int primary key, tenant_id uuid not null, note text);
INSERT INTO cards VALUES (1,'1111...','tenant A row'), (2,'2222...','tenant B row');
ALTER TABLE cards ENABLE ROW LEVEL SECURITY;
ALTER TABLE cards FORCE  ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON cards
  USING (tenant_id = current_setting('app.tenant_id')::uuid);
GRANT SELECT ON cards TO app;

-- fails: ERROR: unrecognized configuration parameter "app.tenant_id"
CREATE MATERIALIZED VIEW mv_mastery AS SELECT tenant_id, count(*) n FROM cards GROUP BY tenant_id;

SET app.tenant_id = '1111...';   -- now it builds, holding tenant A only
CREATE MATERIALIZED VIEW mv_mastery AS SELECT tenant_id, count(*) n FROM cards GROUP BY tenant_id;
CREATE VIEW v_default AS SELECT tenant_id, count(*) n FROM cards GROUP BY tenant_id;
CREATE VIEW v_invoker WITH (security_invoker=true)
                      AS SELECT tenant_id, count(*) n FROM cards GROUP BY tenant_id;
GRANT SELECT ON mv_mastery, v_default, v_invoker TO app;

-- then, connected as app with app.tenant_id = '2222...', read all four.
```

## Sources

- [Keyless entity types — EF Core](https://learn.microsoft.com/en-us/ef/core/modeling/keyless-entity-types)
- [Global query filters — EF Core](https://learn.microsoft.com/en-us/ef/core/querying/filters)
- [PostgreSQL 17 — CREATE VIEW](https://www.postgresql.org/docs/17/sql-createview.html)
- [PostgreSQL 17 — CREATE MATERIALIZED VIEW](https://www.postgresql.org/docs/17/sql-creatematerializedview.html)
- [PostgreSQL 17 — ALTER MATERIALIZED VIEW](https://www.postgresql.org/docs/17/sql-altermaterializedview.html)
- [PostgreSQL 17 — REFRESH MATERIALIZED VIEW](https://www.postgresql.org/docs/17/sql-refreshmaterializedview.html)
- [PostgreSQL 17 — Row Security Policies](https://www.postgresql.org/docs/17/ddl-rowsecurity.html)
- [PostgreSQL 17 — Privileges (`MAINTAIN`)](https://www.postgresql.org/docs/17/ddl-priv.html)
