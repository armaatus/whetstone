using System.Diagnostics.CodeAnalysis;

using DotNet.Testcontainers.Configurations;

using Npgsql;

using Testcontainers.PostgreSql;

namespace Whetstone.Integration.Tests;

/// <summary>
/// Starts a throwaway Postgres with <c>deploy/db-init</c> bind-mounted, under the same conditions
/// the AppHost gives it (ticket 0.6).
/// <para>
/// This is deliberately not the AppHost. <c>/docker-entrypoint-initdb.d</c> runs once, on first
/// initialisation of a data volume, so iterating on the init script against the AppHost's named
/// volume means a <c>docker volume rm whetstone-dev-data</c> between every attempt — and forgetting
/// it does not fail, it silently tests the previous version of the file. A container with no volume
/// has no such state to forget.
/// </para>
/// </summary>
[SuppressMessage("Style", "CA1515:Consider making public types internal",
    Justification = "xUnit resolves IClassFixture<T> against a public test class, so an internal "
                  + "fixture would be less accessible than the interface that names it. The rule's "
                  + "exemption covers types carrying [Fact]; a fixture carries none.")]
public sealed class DatabaseRoleFixture : IAsyncLifetime
{
    public const string Superuser = "postgres";
    public const string MigratorRole = "whetstone_migrator";
    public const string AppRole = "whetstone_app";
    public const string ReadonlyRole = "whetstone_readonly";

    private const string ApplicationDatabase = "whetstone";
    private const int PostgresPort = 5432;

    // Generated per run rather than written down. hooks/pre-commit runs gitleaks over this file,
    // and a committed credential-shaped literal is worth avoiding even when the credential belongs
    // to a container that is destroyed a few seconds later.
    private static readonly string SuperuserSecret = NewSecret();
    private static readonly string MigratorSecret = NewSecret();
    private static readonly string AppSecret = NewSecret();
    private static readonly string ReadonlySecret = NewSecret();

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("pgvector/pgvector:pg17")
        .WithUsername(Superuser)
        .WithPassword(SuperuserSecret)

        // Aspire does not set POSTGRES_DB, so the entrypoint falls back to a database named after
        // the superuser and the init script runs connected to *that*, not to `whetstone`. Setting
        // it to the superuser's name reproduces the fallback. Leave this at the builder's default
        // and the test passes against conditions the AppHost never provides.
        .WithDatabase(Superuser)

        // The AppHost's Postgres resource sets both of these. They are what makes a LOGIN role with
        // no password unable to connect at all, which the init script's guard clause depends on.
        .WithEnvironment("POSTGRES_HOST_AUTH_METHOD", "scram-sha-256")
        .WithEnvironment("POSTGRES_INITDB_ARGS", "--auth-host=scram-sha-256 --auth-local=scram-sha-256")

        .WithEnvironment("WHETSTONE_MIGRATOR_PASSWORD", MigratorSecret)
        .WithEnvironment("WHETSTONE_APP_PASSWORD", AppSecret)
        .WithEnvironment("WHETSTONE_READONLY_PASSWORD", ReadonlySecret)
        .WithBindMount(DbInitScripts.RootPath, "/docker-entrypoint-initdb.d", AccessMode.ReadOnly)
        .Build();

    /// <summary>Connects as the container superuser. For reading catalogues only — never a model for application code.</summary>
    public string SuperuserConnectionString => ConnectionStringFor(Superuser, SuperuserSecret);

    /// <summary>
    /// Connects as the role migrations run as: it owns <c>whetstone</c> and every table a migration
    /// creates in it. Spec 6.5 hands this to a separate one-shot step, never to a running host.
    /// </summary>
    public string MigratorConnectionString => ConnectionStringFor(MigratorRole, MigratorSecret);

    /// <summary>
    /// Connects as the credential the application itself runs on (spec 7.3): DML on tables it does
    /// not own, and nothing else. Also the readiness probe — see <see cref="WaitForInitScriptAsync"/>.
    /// </summary>
    public string AppConnectionString => ConnectionStringFor(AppRole, AppSecret);

    /// <summary>
    /// Connects as the SELECT-only role of spec 7.3. It exists here so that "readonly cannot write"
    /// can be asserted as a real denial against a real login, rather than read off a grant listing.
    /// </summary>
    public string ReadonlyConnectionString => ConnectionStringFor(ReadonlyRole, ReadonlySecret);

    /// <summary>
    /// Starts the container, then blocks until the init script has actually produced something
    /// usable. <c>StartAsync</c> alone only establishes that Postgres accepts connections, which it
    /// does whether or not <c>01-roles.sql</c> achieved anything.
    /// </summary>
    public async Task InitializeAsync()
    {
        // ConfigureAwait(true), not (false): CA2007 demands an explicit choice and xUnit1030
        // rejects (false) because it bypasses the parallelisation limits.
        await _container.StartAsync().ConfigureAwait(true);
        await WaitForInitScriptAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Destroys the container and, with it, the whole database. Nothing is reset between the test
    /// classes that share this fixture because nothing outlives it.
    /// </summary>
    public async Task DisposeAsync() => await _container.DisposeAsync().ConfigureAwait(true);

    /// <summary>
    /// One password, for one container, for a few seconds. Generated rather than written down for
    /// the reason given on the secret fields above.
    /// </summary>
    private static string NewSecret() => "t" + Guid.NewGuid().ToString("N");

    /// <summary>
    /// Builds a connection string for <paramref name="role"/> against the container's mapped port.
    /// <para>
    /// Always to <c>whetstone</c>, never to the superuser-named database the entrypoint falls back
    /// to creating: privileges on schemas, tables and default ACLs are per-database, so a connection
    /// to the wrong one would observe a privilege model nobody wrote.
    /// </para>
    /// </summary>
    private string ConnectionStringFor(string role, string password) => new NpgsqlConnectionStringBuilder
    {
        Host = _container.Hostname,
        Port = _container.GetMappedPublicPort(PostgresPort),
        Database = ApplicationDatabase,
        Username = role,
        Password = password,
    }.ConnectionString;

    /// <summary>
    /// The container is "ready" as soon as Postgres accepts connections, which happens whether or
    /// not the init script did anything useful. Opening a connection as <c>whetstone_app</c> against
    /// <c>whetstone</c> is the real readiness signal: it needs the role, the database and the
    /// CONNECT grant all to exist.
    /// </summary>
    private async Task WaitForInitScriptAsync()
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(90);
        NpgsqlException? last = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                var connection = new NpgsqlConnection(AppConnectionString);
                await using (connection.ConfigureAwait(true))
                {
                    await connection.OpenAsync().ConfigureAwait(true);
                    return;
                }
            }
            catch (NpgsqlException exception)
            {
                last = exception;
                await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(true);
            }
        }

        // A failing statement in an init script aborts startup, and the entrypoint's own output is
        // the only place that says why. Without this the failure reads as a connection timeout.
        var logs = await _container.GetLogsAsync().ConfigureAwait(true);
        throw new InvalidOperationException(
            $"deploy/db-init/01-roles.sql did not leave a usable {AppRole} role in {ApplicationDatabase}. "
            + $"Container output follows.{Environment.NewLine}{logs.Stdout}{logs.Stderr}",
            last);
    }
}
