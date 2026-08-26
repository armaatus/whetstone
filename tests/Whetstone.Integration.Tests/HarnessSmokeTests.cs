using Testcontainers.PostgreSql;

namespace Whetstone.Integration.Tests;

/// <summary>
/// Ticket 0.3 wires Testcontainers into this project. This test starts a real
/// Postgres and is therefore also the canary for a misconfigured Docker endpoint:
/// Testcontainers resolves its host from DOCKER_HOST, then
/// <c>~/.testcontainers.properties</c>, then the active <c>docker context</c>. A
/// Java-style escaped colon in that properties file (<c>unix\://...</c>) parses as
/// a relative URI and fails here with a TypeInitializationException rather than
/// anything mentioning Docker.
/// </summary>
public class HarnessSmokeTests
{
    [Fact]
    public async Task A_postgres_container_starts_and_yields_a_connection_string()
    {
        var container = new PostgreSqlBuilder("postgres:17").Build();

        // ConfigureAwait(true), not (false): CA2007 demands an explicit choice and
        // xUnit1030 rejects (false) because it bypasses the parallelisation limits.
        await container.StartAsync().ConfigureAwait(true);
        try
        {
            Assert.Contains("Host=", container.GetConnectionString(), StringComparison.Ordinal);
        }
        finally
        {
            await container.DisposeAsync().ConfigureAwait(true);
        }
    }
}
