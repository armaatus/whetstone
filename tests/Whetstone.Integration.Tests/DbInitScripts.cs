namespace Whetstone.Integration.Tests;

/// <summary>
/// Locates <c>deploy/db-init</c> for the tests that bind-mount it into a container.
/// </summary>
internal static class DbInitScripts
{
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
    public static string RootPath { get; } = Resolve();

    private static string Resolve()
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
}
