-- ============================================================================
-- Runs ONCE, from /docker-entrypoint-initdb.d, on first initialisation of the
-- data volume. Editing this file after the volume exists does nothing and says
-- nothing. See deploy/db-init/README.md before you debug a change that had no
-- effect.
--
-- The point of this file: if the application develops connected as the
-- superuser or as the table owner, RLS policies do not apply to it, and every
-- tenant-isolation bug is invisible locally and appears first in production.
-- Nothing in the solution may connect as a superuser or as the table owner.
--
-- ---------------------------------------------------------------------------
-- The four decisions this file makes, and why
-- ---------------------------------------------------------------------------
--
-- 1. `whetstone` is created HERE, owned by whetstone_migrator.
--
--    Aspire does not set POSTGRES_DB, so this script is connected to the
--    default database (named after the superuser), not to `whetstone`.
--    Aspire's AddDatabase("whetstone") issues CREATE DATABASE from the host
--    only after initdb has finished — i.e. after the one and only window in
--    which anything here runs as superuser has already closed.
--
--    CREATE EXTENSION is per-database and needs superuser. So if `whetstone`
--    did not exist inside this window, "pgvector enabled at initialisation"
--    would have nowhere to be true. Creating it here is what makes that claim
--    possible. Aspire then hits 42P04 duplicate_database, which it handles and
--    logs as "Database 'whetstone' already exists" — expected, not an error.
--
--    Owning the database is not cosmetic. Since PostgreSQL 15 the `public`
--    schema is owned by the pseudo-role `pg_database_owner`, which resolves
--    dynamically to whoever owns the current database. Making the migrator the
--    database owner therefore makes it the owner of `public`, with CREATE on
--    it, without a single explicit grant. The trap the other way round: a
--    migrator that is NOT the database owner has no CREATE on `public` at all
--    under PG15+, and the grant that looks like it fixes that is one the
--    superuser can write here and no one can write in production.
--
-- 2. Passwords come from the environment, never from this file.
--
--    hooks/pre-commit runs gitleaks over everything staged, and this file is
--    committed. `\getenv` reads them at initdb time; the AppHost supplies them
--    from Aspire parameters persisted to user secrets (so they stay stable
--    across restarts, which matters because the roles are only created once).
--    scram-sha-256 is the auth method, so a LOGIN role with no password cannot
--    connect at all — a variable that is unset *or empty* must fail loudly, and
--    does, below.
--
-- 3. whetstone_app gets SELECT, INSERT, UPDATE, DELETE. Nothing else.
--
--    Enumerated rather than ALL, and three omissions are deliberate:
--      * TRUNCATE  — has no row-security policy. A role holding it can empty a
--                    table across every tenant regardless of RLS. This is the
--                    one that matters (ADR-0003).
--      * REFERENCES, TRIGGER — both exist to create schema objects. An
--                    application that never runs DDL never needs them.
--    No sequence privileges are granted: §6.1 puts key generation in the
--    application (Guid.CreateVersion7()), so there are none to grant. A future
--    migration that adds a sequence will fail loudly for whetstone_app, which
--    is the correct outcome — that is a decision to take deliberately then,
--    not one to pre-approve now.
--
-- 4. Future tables are covered by ALTER DEFAULT PRIVILEGES FOR ROLE
--    whetstone_migrator, the migrator, not the superuser running this script.
--
--    Every table in §6.2 is created by a migration that does not exist yet.
--    Default privileges key off the role that CREATES the object, and the
--    FOR ROLE clause defaults to the current role. Omit it and this file grants
--    a future privilege on tables created by `postgres`, which is nothing that
--    will ever happen. It passes every test that inspects the catalogue and
--    fails on the first migration in Epic 2. Hence the acceptance criterion
--    that the positive control uses a table created AFTER this script ran.
--
-- ---------------------------------------------------------------------------
-- Adding a fourth role
-- ---------------------------------------------------------------------------
-- #129 (2.15) adds whetstone_generation. The sections below are ordered
-- cluster-scoped → database-scoped → schema-scoped → object-scoped, so a fourth
-- role is one addition per section and no edits to the first three. It still
-- costs everyone a volume reset, because this file runs once.
--
-- Not in this file: policies, ENABLE/FORCE ROW LEVEL SECURITY and the
-- tenant_isolation predicate. Those are #28, which also has to create these
-- same three roles in production.
-- ============================================================================

\set ON_ERROR_STOP on


-- ---------------------------------------------------------------------------
-- 0. Passwords, from the environment
-- ---------------------------------------------------------------------------
-- Two ways to arrive here without a usable password, and they have to be one
-- check rather than two:
--
--   * absent      — \getenv leaves the psql variable unset, and :{?name} is false.
--   * present but empty — \getenv sets the variable to '', and :{?name} is TRUE.
--
-- The second is the dangerous one. `PASSWORD ''` is not rejected: PostgreSQL
-- stores it as a null password, so all three roles get created, the database
-- gets created, initdb reports success, and the container comes up healthy.
-- The first symptom is the application failing scram-sha-256 authentication
-- against roles that plainly exist — the failure lands as far as possible from
-- its cause. So normalise "unset" to "empty" and reject the single condition.

\getenv migrator_password WHETSTONE_MIGRATOR_PASSWORD
\getenv app_password      WHETSTONE_APP_PASSWORD
\getenv readonly_password WHETSTONE_READONLY_PASSWORD

\if :{?migrator_password}
\else
\set migrator_password ''
\endif

\if :{?app_password}
\else
\set app_password ''
\endif

\if :{?readonly_password}
\else
\set readonly_password ''
\endif

-- \gset rather than three more \if :{?...}: psql's \if takes a value, not an
-- expression, so anything about the *contents* of a variable has to be decided
-- by the server. ::text so the branch reads 'true'/'false' rather than 't'/'f'.

SELECT (:'migrator_password' = '')::text AS migrator_password_missing,
       (:'app_password'      = '')::text AS app_password_missing,
       (:'readonly_password' = '')::text AS readonly_password_missing
\gset

\if :migrator_password_missing
DO $$ BEGIN
    RAISE EXCEPTION
        'WHETSTONE_MIGRATOR_PASSWORD is unset or empty. scram-sha-256 is the auth method: a LOGIN role without a password can never connect. See deploy/db-init/README.md.';
END $$;
\endif

\if :app_password_missing
DO $$ BEGIN
    RAISE EXCEPTION
        'WHETSTONE_APP_PASSWORD is unset or empty. scram-sha-256 is the auth method: a LOGIN role without a password can never connect. See deploy/db-init/README.md.';
END $$;
\endif

\if :readonly_password_missing
DO $$ BEGIN
    RAISE EXCEPTION
        'WHETSTONE_READONLY_PASSWORD is unset or empty. scram-sha-256 is the auth method: a LOGIN role without a password can never connect. See deploy/db-init/README.md.';
END $$;
\endif


-- ---------------------------------------------------------------------------
-- 1. Roles — cluster-scoped
-- ---------------------------------------------------------------------------
-- CREATE ROLE is the only thing in this file that is cluster-wide. Everything
-- from section 2 down lands in whichever database is connected at that line.
--
-- Every attribute is stated rather than left to the default, because the
-- defaults are the assertion: NOBYPASSRLS on all three is ADR-0003 §5 — "the
-- strongest form of audited bypass is not having one" — and that includes the
-- migrator. Nothing in Whetstone may hold BYPASSRLS.
--
-- INHERIT rather than NOINHERIT: none of the three is a member of any role
-- today, so it changes nothing now, and it leaves group roles usable later
-- without a SET ROLE dance. It is not a way round RLS either way — role
-- attributes such as BYPASSRLS and SUPERUSER are never inherited through
-- membership, and policies are evaluated against current_user.

CREATE ROLE whetstone_migrator
    LOGIN PASSWORD :'migrator_password'
    NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS INHERIT;

CREATE ROLE whetstone_app
    LOGIN PASSWORD :'app_password'
    NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS INHERIT;

CREATE ROLE whetstone_readonly
    LOGIN PASSWORD :'readonly_password'
    NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS INHERIT;

COMMENT ON ROLE whetstone_migrator IS
    'Owns the whetstone database and its schema. Runs migrations (spec 6.5). DDL only — never the application connection.';
COMMENT ON ROLE whetstone_app  IS
    'The application connection (spec 7.3). DML only. Not a table owner, no BYPASSRLS: RLS policies apply to it.';
COMMENT ON ROLE whetstone_readonly IS
    'Ad-hoc querying. SELECT only, and subject to RLS — what it sees is what the app sees for the tenant in app.tenant_id.';


-- ---------------------------------------------------------------------------
-- 2. The application database
-- ---------------------------------------------------------------------------
-- Owned by the migrator, which makes it the owner of `public` via
-- pg_database_owner. See decision 1 in the header.

CREATE DATABASE whetstone OWNER whetstone_migrator;

\connect whetstone

-- Per-database, and superuser-only. This is the window that exists for it.
-- Enabled now though the MVP does not use it: adding an extension later means a
-- migration needing superuser on a running production database (§13.4).
CREATE EXTENSION IF NOT EXISTS vector;

-- CONNECT and TEMPORARY are granted to PUBLIC on every new database. Inheriting
-- that is a decision by default; this is the decision made explicitly. Revoking
-- TEMPORARY also closes CREATE TEMP TABLE as a DDL path for whetstone_app.
REVOKE ALL ON DATABASE whetstone FROM PUBLIC;
GRANT CONNECT ON DATABASE whetstone
    TO whetstone_migrator, whetstone_app, whetstone_readonly;


-- ---------------------------------------------------------------------------
-- 3. The schema
-- ---------------------------------------------------------------------------
-- No grant of CREATE to whetstone_migrator appears here, and that is the point:
-- it holds CREATE on `public` by owning it through pg_database_owner. A grant
-- would work locally and hide whether the ownership model is actually right.
--
-- USAGE, and only USAGE, for the other two. Without CREATE on this schema,
-- CREATE TABLE as whetstone_app raises 42501 — which is the acceptance
-- criterion, and it holds for every table name, not just the ones under test.

REVOKE ALL ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA public TO whetstone_app, whetstone_readonly;


-- ---------------------------------------------------------------------------
-- 4. Tables that exist now
-- ---------------------------------------------------------------------------
-- None yet — §6.2's tables all arrive by migration. These two statements are
-- no-ops today and stay correct if a later db-init file seeds anything.

GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO whetstone_app;
GRANT SELECT                        ON ALL TABLES IN SCHEMA public TO whetstone_readonly;


-- ---------------------------------------------------------------------------
-- 5. Tables that do not exist yet
-- ---------------------------------------------------------------------------
-- FOR ROLE whetstone_migrator is load-bearing. See decision 4 in the header.
-- ON TABLES covers views and materialised views too, which is what keeps
-- whetstone_readonly useful against anything a migration adds.

ALTER DEFAULT PRIVILEGES FOR ROLE whetstone_migrator IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO whetstone_app;

ALTER DEFAULT PRIVILEGES FOR ROLE whetstone_migrator IN SCHEMA public
    GRANT SELECT ON TABLES TO whetstone_readonly;
