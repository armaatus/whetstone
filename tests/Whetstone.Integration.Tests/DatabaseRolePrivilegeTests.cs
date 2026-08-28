using System.Diagnostics.CodeAnalysis;
using System.Globalization;

using Npgsql;

namespace Whetstone.Integration.Tests;

/// <summary>
/// Ticket 0.6 — the three roles of spec 7.3, asserted against a real Postgres running
/// <c>deploy/db-init/01-roles.sql</c>.
/// <para>
/// Every claim here is read from the catalogues or produced by an actual denial, never from the
/// text of the SQL file. The distinction matters most for the "objects that do not exist yet"
/// tests: inspecting <c>pg_default_acl</c> proves the statement was typed, whereas creating a table
/// after the script has finished and then asking <c>has_table_privilege</c> proves the guarantee.
/// It is the second one that fails when <c>ALTER DEFAULT PRIVILEGES</c> is missing its
/// <c>FOR ROLE</c> clause, and the first one that would let that ship.
/// </para>
/// </summary>
/// <remarks>
/// SQLSTATE 42501 is <c>insufficient_privilege</c>. Npgsql surfaces it as
/// <see cref="PostgresException.SqlState"/>.
/// </remarks>
[SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities",
    Justification = "Every statement executed here is a literal declared in this file. There is no "
                  + "external input, and the point of these tests is to run DDL and DML as several "
                  + "different roles, which parameters cannot express.")]
public sealed class DatabaseRolePrivilegeTests(DatabaseRoleFixture fixture)
    : IClassFixture<DatabaseRoleFixture>
{
    private const string Denied = "42501";

    // ---------------------------------------------------------------------
    // Positive control. A privilege model in which nothing works passes every
    // negative test in this file.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Migrator_can_create_a_table()
    {
        await CreateTableAsMigratorAsync("migrator_ddl_probe").ConfigureAwait(true);

        var exists = await ScalarAsync<bool>(
            fixture.SuperuserConnectionString,
            "SELECT to_regclass('public.migrator_ddl_probe') IS NOT NULL;").ConfigureAwait(true);

        Assert.True(exists, "whetstone_migrator must be able to run DDL — it is the role migrations run as.");
    }

    [Fact]
    public async Task App_can_insert_select_update_and_delete_a_table_created_after_the_init_script_ran()
    {
        await CreateTableAsMigratorAsync("app_dml_probe").ConfigureAwait(true);

        await ExecuteAsync(fixture.AppConnectionString, """
            INSERT INTO app_dml_probe (id, tenant_id, body) VALUES (gen_random_uuid(), gen_random_uuid(), 'before');
            UPDATE app_dml_probe SET body = 'after';
            """).ConfigureAwait(true);

        var body = await ScalarAsync<string>(
            fixture.AppConnectionString, "SELECT body FROM app_dml_probe;").ConfigureAwait(true);
        Assert.Equal("after", body);

        await ExecuteAsync(fixture.AppConnectionString, "DELETE FROM app_dml_probe;").ConfigureAwait(true);

        var remaining = await ScalarAsync<long>(
            fixture.AppConnectionString, "SELECT count(*) FROM app_dml_probe;").ConfigureAwait(true);
        Assert.Equal(0, remaining);
    }

    [Fact]
    public async Task Readonly_can_select_a_table_created_after_the_init_script_ran()
    {
        await CreateTableAsMigratorAsync("readonly_select_probe").ConfigureAwait(true);

        var count = await ScalarAsync<long>(
            fixture.ReadonlyConnectionString, "SELECT count(*) FROM readonly_select_probe;").ConfigureAwait(true);

        Assert.Equal(0, count);
    }

    /// <summary>
    /// The Epic 2 failure mode, stated as a privilege rather than as a successful query: every table
    /// in spec 6.2 arrives by a migration written long after this grant. If default privileges were
    /// recorded against the wrong grantor, this is where it shows.
    /// </summary>
    [Theory]
    [InlineData(DatabaseRoleFixture.AppRole, "SELECT")]
    [InlineData(DatabaseRoleFixture.AppRole, "INSERT")]
    [InlineData(DatabaseRoleFixture.AppRole, "UPDATE")]
    [InlineData(DatabaseRoleFixture.AppRole, "DELETE")]
    [InlineData(DatabaseRoleFixture.ReadonlyRole, "SELECT")]
    public async Task A_migration_adding_a_table_grants_it_without_anyone_editing_the_init_script(
        string role, string privilege)
    {
        await CreateTableAsMigratorAsync("future_object_probe").ConfigureAwait(true);

        var granted = await ScalarAsync<bool>(
            fixture.SuperuserConnectionString,
            $"SELECT has_table_privilege('{role}', 'public.future_object_probe', '{privilege}');")
            .ConfigureAwait(true);

        Assert.True(granted, $"{role} lost {privilege} on a table created after 01-roles.sql ran.");
    }

    // ---------------------------------------------------------------------
    // Negative control.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task App_cannot_create_a_table()
    {
        var exception = await DeniedAsync(
            fixture.AppConnectionString, "CREATE TABLE app_ddl_attempt (id int);").ConfigureAwait(true);

        Assert.Equal(Denied, exception.SqlState);
    }

    /// <summary>
    /// The other DDL route into the database. Closed by revoking TEMPORARY on the database from
    /// PUBLIC, which is a privilege every new database grants by default.
    /// </summary>
    [Fact]
    public async Task App_cannot_create_a_temporary_table()
    {
        var exception = await DeniedAsync(
            fixture.AppConnectionString, "CREATE TEMPORARY TABLE app_temp_attempt (id int);").ConfigureAwait(true);

        Assert.Equal(Denied, exception.SqlState);
    }

    /// <summary>
    /// TRUNCATE has no row-security policy: a role holding it empties the table for every tenant,
    /// whatever the policies added by #28 say. It is withheld for that reason and not because the
    /// application has no use for it.
    /// </summary>
    [Fact]
    public async Task App_cannot_truncate()
    {
        await CreateTableAsMigratorAsync("app_truncate_probe").ConfigureAwait(true);

        var exception = await DeniedAsync(
            fixture.AppConnectionString, "TRUNCATE app_truncate_probe;").ConfigureAwait(true);

        Assert.Equal(Denied, exception.SqlState);
    }

    [Theory]
    [InlineData("INSERT INTO readonly_write_probe (id, tenant_id, body) VALUES (gen_random_uuid(), gen_random_uuid(), 'x');")]
    [InlineData("UPDATE readonly_write_probe SET body = 'x';")]
    [InlineData("DELETE FROM readonly_write_probe;")]
    public async Task Readonly_cannot_write(string statement)
    {
        await CreateTableAsMigratorAsync("readonly_write_probe").ConfigureAwait(true);

        var exception = await DeniedAsync(fixture.ReadonlyConnectionString, statement).ConfigureAwait(true);

        Assert.Equal(Denied, exception.SqlState);
    }

    // ---------------------------------------------------------------------
    // Catalogue assertions — spec 7.3 and ADR-0003 §5.
    // ---------------------------------------------------------------------

    /// <summary>
    /// Spec 7.3: <c>whetstone_app</c> is not the table owner. An owner bypasses its own policies
    /// unless the table carries FORCE ROW LEVEL SECURITY, so this is half of what makes the
    /// isolation added by #28 real rather than decorative.
    /// </summary>
    [Fact]
    public async Task App_is_not_the_owner_of_a_table_a_migration_creates()
    {
        await CreateTableAsMigratorAsync("ownership_probe").ConfigureAwait(true);

        var owner = await ScalarAsync<string>(
            fixture.SuperuserConnectionString,
            "SELECT tableowner FROM pg_tables WHERE schemaname = 'public' AND tablename = 'ownership_probe';")
            .ConfigureAwait(true);

        Assert.Equal(DatabaseRoleFixture.MigratorRole, owner);
        Assert.NotEqual(DatabaseRoleFixture.AppRole, owner);
    }

    /// <summary>
    /// ADR-0003 §5: no BYPASSRLS role exists at all — "the strongest form of audited bypass is not
    /// having one". That covers the migrator too, which is why this asserts over every whetstone
    /// role rather than only over the one the application uses.
    /// </summary>
    [Fact]
    public async Task No_whetstone_role_holds_bypassrls_or_superuser()
    {
        var offenders = await ScalarAsync<long>(
            fixture.SuperuserConnectionString, """
            SELECT count(*) FROM pg_roles
            WHERE rolname LIKE 'whetstone%' AND (rolbypassrls OR rolsuper);
            """).ConfigureAwait(true);

        Assert.Equal(0, offenders);
    }

    [Theory]
    [InlineData(DatabaseRoleFixture.MigratorRole)]
    [InlineData(DatabaseRoleFixture.AppRole)]
    [InlineData(DatabaseRoleFixture.ReadonlyRole)]
    public async Task Whetstone_roles_hold_no_cluster_administration_attributes(string role)
    {
        var attributes = await ScalarAsync<long>(
            fixture.SuperuserConnectionString,
            $"""
            SELECT count(*) FROM pg_roles
            WHERE rolname = '{role}'
              AND (rolsuper OR rolcreatedb OR rolcreaterole OR rolreplication OR rolbypassrls);
            """).ConfigureAwait(true);

        Assert.Equal(0, attributes);
    }

    [Fact]
    public async Task App_holds_no_create_privilege_on_the_schema_it_works_in()
    {
        var canCreate = await ScalarAsync<bool>(
            fixture.SuperuserConnectionString,
            "SELECT has_schema_privilege('whetstone_app', 'public', 'CREATE');").ConfigureAwait(true);

        Assert.False(canCreate);
    }

    /// <summary>
    /// Since PostgreSQL 15 the <c>public</c> schema belongs to <c>pg_database_owner</c>, which
    /// resolves to whoever owns the current database. The migrator's CREATE right therefore comes
    /// from owning <c>whetstone</c> and not from any grant — and an explicit grant would hide a
    /// broken ownership model behind a privilege that happens to work locally.
    /// </summary>
    [Fact]
    public async Task Migrator_holds_create_on_public_by_owning_the_database()
    {
        var databaseOwner = await ScalarAsync<string>(
            fixture.SuperuserConnectionString,
            "SELECT pg_get_userbyid(datdba) FROM pg_database WHERE datname = 'whetstone';").ConfigureAwait(true);
        Assert.Equal(DatabaseRoleFixture.MigratorRole, databaseOwner);

        var schemaOwner = await ScalarAsync<string>(
            fixture.SuperuserConnectionString,
            "SELECT pg_get_userbyid(nspowner) FROM pg_namespace WHERE nspname = 'public';").ConfigureAwait(true);
        Assert.Equal("pg_database_owner", schemaOwner);

        var canCreate = await ScalarAsync<bool>(
            fixture.SuperuserConnectionString,
            "SELECT has_schema_privilege('whetstone_migrator', 'public', 'CREATE');").ConfigureAwait(true);
        Assert.True(canCreate);
    }

    /// <summary>CONNECT is granted to PUBLIC on every new database. Spec 13.4 says decide, not inherit.</summary>
    [Fact]
    public async Task Public_cannot_connect_to_the_application_database()
    {
        var canConnect = await ScalarAsync<bool>(
            fixture.SuperuserConnectionString,
            "SELECT has_database_privilege('public', 'whetstone', 'CONNECT');").ConfigureAwait(true);

        Assert.False(canConnect);
    }

    // ---------------------------------------------------------------------
    // What the rest of Epic 0 and Epic 2 depend on.
    // ---------------------------------------------------------------------

    /// <summary>
    /// Spec 13.4 / ADR-0005: the extension is enabled at initialisation because adding it later
    /// means a migration needing superuser on a running production database. CREATE EXTENSION is
    /// per-database, so the claim is about <c>whetstone</c> — which is why 01-roles.sql has to
    /// create that database inside the one window where it is connected as superuser.
    /// </summary>
    [Fact]
    public async Task The_vector_extension_is_installed_in_the_application_database()
    {
        var installed = await ScalarAsync<bool>(
            fixture.SuperuserConnectionString,
            "SELECT EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'vector');").ConfigureAwait(true);

        Assert.True(installed);
    }

    /// <summary>
    /// #29 (2.6) has the EF Core interceptor set <c>app.tenant_id</c> on every connection, and the
    /// policies in #28 read it. Custom GUCs in a namespace of their own need no privilege — this
    /// records that, so #29 does not have to discover it or grant something unnecessary.
    /// </summary>
    [Fact]
    public async Task App_can_set_the_tenant_guc_without_holding_any_privilege_for_it()
    {
        var tenant = Guid.NewGuid();

        var observed = await ScalarAsync<string>(fixture.AppConnectionString,
            $"""
            SET app.tenant_id = '{tenant}';
            SELECT current_setting('app.tenant_id');
            """).ConfigureAwait(true);

        Assert.Equal(tenant.ToString(), observed);
    }

    // ---------------------------------------------------------------------

    private async Task CreateTableAsMigratorAsync(string table) =>
        await ExecuteAsync(fixture.MigratorConnectionString,
            $"CREATE TABLE IF NOT EXISTS {table} (id uuid PRIMARY KEY, tenant_id uuid NOT NULL, body text);")
            .ConfigureAwait(true);

    // The nesting is CA2007: `await using var` gives the analyser nowhere to put a ConfigureAwait,
    // and the repo treats warnings as errors.
    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        var connection = new NpgsqlConnection(connectionString);
        await using (connection.ConfigureAwait(true))
        {
            await connection.OpenAsync().ConfigureAwait(true);

            var command = new NpgsqlCommand(sql, connection);
            await using (command.ConfigureAwait(true))
            {
                await command.ExecuteNonQueryAsync().ConfigureAwait(true);
            }
        }
    }

    private static async Task<T> ScalarAsync<T>(string connectionString, string sql)
    {
        var connection = new NpgsqlConnection(connectionString);
        await using (connection.ConfigureAwait(true))
        {
            await connection.OpenAsync().ConfigureAwait(true);

            var command = new NpgsqlCommand(sql, connection);
            await using (command.ConfigureAwait(true))
            {
                var value = await command.ExecuteScalarAsync().ConfigureAwait(true);
                return (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture)!;
            }
        }
    }

    private static async Task<PostgresException> DeniedAsync(string connectionString, string sql) =>
        await Assert.ThrowsAsync<PostgresException>(
            () => ExecuteAsync(connectionString, sql)).ConfigureAwait(true);
}
