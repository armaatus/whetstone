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

    private static readonly string DbInitDirectory = ResolveDbInitDirectory();

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
        .WithBindMount(DbInitDirectory, "/docker-entrypoint-initdb.d", AccessMode.ReadOnly)
        .Build();

    /// <summary>Connects as the container superuser. For reading catalogues only — never a model for application code.</summary>
    public string SuperuserConnectionString => ConnectionStringFor(Superuser, SuperuserSecret);

    public string MigratorConnectionString => ConnectionStringFor(MigratorRole, MigratorSecret);

    public string AppConnectionString => ConnectionStringFor(AppRole, AppSecret);

    public string ReadonlyConnectionString => ConnectionStringFor(ReadonlyRole, ReadonlySecret);

    public async Task InitializeAsync()
    {
        // ConfigureAwait(true), not (false): CA2007 demands an explicit choice and xUnit1030
        // rejects (false) because it bypasses the parallelisation limits.
        await _container.StartAsync().ConfigureAwait(true);
        await WaitForInitScriptAsync().ConfigureAwait(true);
    }

    public async Task DisposeAsync() => await _container.DisposeAsync().ConfigureAwait(true);

    private static string NewSecret() => "t" + Guid.NewGuid().ToString("N");

    /// <summary>
    /// Walks up from the test assembly to the directory holding <c>deploy/db-init</c>.
    /// <para>
    /// Deliberately not <c>CommonDirectoryPath.GetGitDirectory()</c>, and not any of its siblings:
    /// they all resolve a <c>[CallerFilePath]</c>, which is a path baked in at compile time.
    /// Directory.Build.props sets <c>ContinuousIntegrationBuild</c> when <c>CI</c> is set, which
    /// turns on deterministic source paths and rewrites that literal to <c>/_/tests/...</c> — a
    /// path that exists on no disk. The result passes locally and fails on the build server, which
    /// is the worst way round. Reproduce it with <c>CI=true dotnet test</c>.
    /// </para>
    /// </summary>
    private static string ResolveDbInitDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "deploy", "db-init");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException(
            $"No 'deploy/db-init' directory above '{AppContext.BaseDirectory}'. These tests run the "
            + "real init script and cannot assert anything without it.");
    }

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
