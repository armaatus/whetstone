using DotNet.Testcontainers.Configurations;

using Testcontainers.PostgreSql;

namespace Whetstone.Integration.Tests;

/// <summary>
/// Ticket 0.6 — the guard clause at the top of <c>deploy/db-init/01-roles.sql</c>.
/// <para>
/// The rest of the suite asserts what a correctly-supplied privilege model produces.
/// This asserts what happens when it is supplied wrongly, which is the case that has no
/// natural failure: <c>CREATE ROLE … PASSWORD ''</c> is not an error, it is stored as a
/// null password. Without the guard the roles, the database and the grants would all be
/// created, initdb would report success, the container would come up healthy, and the
/// only symptom would be the application failing scram-sha-256 authentication against
/// roles that plainly exist.
/// </para>
/// <para>
/// Each case therefore starts its own container — the failure being asserted is a failure
/// to start, so it cannot share <see cref="DatabaseRoleFixture"/>'s.
/// </para>
/// </summary>
public sealed class DatabaseInitPasswordGuardTests
{
    private const string Superuser = "postgres";

    private static readonly string[] Variables =
    [
        "WHETSTONE_MIGRATOR_PASSWORD",
        "WHETSTONE_APP_PASSWORD",
        "WHETSTONE_READONLY_PASSWORD",
    ];

    [Theory]
    [InlineData("WHETSTONE_MIGRATOR_PASSWORD", "")]
    [InlineData("WHETSTONE_APP_PASSWORD", "")]
    [InlineData("WHETSTONE_READONLY_PASSWORD", "")]

    // Absent, not empty: \getenv leaves the psql variable unset rather than setting it to '',
    // so this reaches the check by a different route and is the regression guard for the
    // normalisation that funnels both into one condition.
    [InlineData("WHETSTONE_APP_PASSWORD", null)]
    public async Task Initialisation_aborts_when_a_role_password_is_empty_or_absent(
        string variable, string? value)
    {
        var container = BuildContainer(variable, value);

        // ConfigureAwait(true), not (false): CA2007 demands an explicit choice and xUnit1030
        // rejects (false) because it bypasses the parallelisation limits.
        await using (container.ConfigureAwait(true))
        {
            // Half the assertion: a failing statement in an init script aborts the entrypoint, so
            // the container never becomes ready and StartAsync gives up on it. Without the guard
            // this line is what fails — the container comes up perfectly healthy.
            await Assert.ThrowsAnyAsync<Exception>(
                async () => await container.StartAsync().ConfigureAwait(true)).ConfigureAwait(true);

            // The other half. "Did not start" on its own would also be satisfied by a typo, a bad
            // mount or an image that will not run; only the entrypoint's own output says the script
            // stopped where it meant to, and about which variable.
            var logs = await container.GetLogsAsync().ConfigureAwait(true);

            Assert.Contains(
                $"{variable} is unset or empty",
                logs.Stdout + logs.Stderr,
                StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Every variable supplied except <paramref name="variable"/>, which is supplied empty or —
    /// when <paramref name="value"/> is null — not supplied at all.
    /// <para>
    /// Everything else matches <see cref="DatabaseRoleFixture"/>, including the two auth settings:
    /// the guard exists because of <c>scram-sha-256</c>, and under <c>trust</c> a null password is
    /// not a problem at all, so a container without them would pass this test for the wrong reason.
    /// </para>
    /// </summary>
    private static PostgreSqlContainer BuildContainer(string variable, string? value)
    {
        var builder = new PostgreSqlBuilder("pgvector/pgvector:pg17")
            .WithUsername(Superuser)
            .WithPassword(NewSecret())
            .WithDatabase(Superuser)
            .WithEnvironment("POSTGRES_HOST_AUTH_METHOD", "scram-sha-256")
            .WithEnvironment("POSTGRES_INITDB_ARGS", "--auth-host=scram-sha-256 --auth-local=scram-sha-256")
            .WithBindMount(DbInitScripts.RootPath, "/docker-entrypoint-initdb.d", AccessMode.ReadOnly);

        foreach (var name in Variables)
        {
            if (name != variable)
            {
                builder = builder.WithEnvironment(name, NewSecret());
            }
            else if (value is not null)
            {
                builder = builder.WithEnvironment(name, value);
            }
        }

        return builder.Build();
    }

    private static string NewSecret() => "t" + Guid.NewGuid().ToString("N");
}
