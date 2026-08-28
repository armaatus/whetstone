# `deploy/db-init`

Everything in this directory is mounted read-only at `/docker-entrypoint-initdb.d` by
`src/Whetstone.AppHost/AppHost.cs`. The Postgres entrypoint runs `*.sql` in filename order.

## The one thing to know first

**These scripts run once** — on first initialisation of the data volume, and never again. Editing
`01-roles.sql` while `whetstone-dev-data` already exists changes nothing, reports nothing, and
leaves you debugging the previous version of the file. That failure is silent, which is the
expensive part.

A real reset is:

```sh
# stop the AppHost first — the volume is in use while it runs
docker volume rm whetstone-dev-data
```

## Iterating on the SQL

Fastest to slowest. Prefer the first one that can answer your question.

**1. The integration tests.** `dotnet test tests/Whetstone.Integration.Tests`. Each run gets a fresh
container with the same bind mount and no named volume, so there is no reset to forget — and it is
the same code path CI runs. `DatabaseRolePrivilegeTests` is the specification of what these scripts
have to produce.

**2. A throwaway container**, when you want an interactive `psql` to poke at a catalogue rather than
compile an assertion:

```sh
docker run --rm -p 5434:5432 \
  -e POSTGRES_PASSWORD=throwaway \
  -e POSTGRES_HOST_AUTH_METHOD=scram-sha-256 \
  -e POSTGRES_INITDB_ARGS="--auth-host=scram-sha-256 --auth-local=scram-sha-256" \
  -e WHETSTONE_MIGRATOR_PASSWORD=throwaway \
  -e WHETSTONE_APP_PASSWORD=throwaway \
  -e WHETSTONE_READONLY_PASSWORD=throwaway \
  -v "$PWD/deploy/db-init:/docker-entrypoint-initdb.d:ro" \
  pgvector/pgvector:pg17
```

No `POSTGRES_DB`, deliberately: Aspire does not set it either, and the database the init script is
connected to is the whole basis of what these scripts can and cannot do. Then
`psql -h localhost -p 5434 -U whetstone_app -d whetstone`.

**3. The AppHost**, once, at the end — to confirm the wiring rather than the SQL.

## When something goes wrong

A failing statement in an init script **aborts container startup**. Aspire's dashboard will tell you
the `postgres` resource is unhealthy and nothing more; the actual error is in the container log:

```sh
docker logs <container>
```

## Passwords

The three role passwords are read from the environment with `\getenv`, never written in the SQL —
this directory is committed and `hooks/pre-commit` runs gitleaks over it. The AppHost supplies them
from Aspire parameters that are generated once and persisted to the AppHost project's user secrets,
so they stay stable across restarts. They have to: the roles are created once, with whatever
password was in the environment at that moment, and a regenerated value would leave the application
unable to authenticate against roles it created itself.

If a variable is missing the script raises and startup aborts, rather than creating a `LOGIN` role
that — under `scram-sha-256` — could never connect at all.

## What lives here

| File | |
| --- | --- |
| `01-roles.sql` | The three roles of spec §7.3, the `whetstone` database, pgvector, and the grants. Ticket 0.6. |

Row-level security — policies, `ENABLE`/`FORCE ROW LEVEL SECURITY`, the `tenant_isolation`
predicate — is **not** here. It arrives with the migrations, in #28, because it has to exist in
production too and production has no `docker-entrypoint-initdb.d`.
