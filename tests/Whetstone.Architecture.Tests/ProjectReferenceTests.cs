using System.Xml.Linq;

namespace Whetstone.Architecture.Tests;

/// <summary>
/// The reference-level half of ARCH-1.
///
/// <see cref="LayeringTests"/> reads compiled IL, and the C# compiler trims assembly references
/// that no type actually uses. A forbidden ProjectReference that is merely *declared* therefore
/// shows up in neither the IL nor <c>Assembly.GetReferencedAssemblies()</c> — it is invisible to
/// every check built on either. The csproj XML is the only place declared intent survives.
///
/// A ProjectReference nobody has used yet violates nothing at runtime, which is exactly what
/// makes it worth catching: it is the loaded gun. The moment someone writes the first `using`,
/// the wrong thing compiles.
/// </summary>
public class ProjectReferenceTests
{
    /// <summary>
    /// Spec 5.1, as data. An empty array means the project may reference no other solution
    /// project at all.
    ///
    /// ServiceDefaults appears on Web and Worker but not in the spec's own list: it arrived with
    /// Aspire in ADR-0005, after 5.1 was written. Every other entry is 5.1 verbatim.
    ///
    /// This table is deliberately tight — it encodes what each project may reference, not what
    /// happens to compile. Infrastructure reaching Domain directly would violate nothing
    /// architecturally, but it is not what 5.1 says, so widening this table is a decision that
    /// has to be made in a diff rather than by accident.
    /// </summary>
    private static readonly Dictionary<string, string[]> Allowed = new(StringComparer.Ordinal)
    {
        ["Whetstone.Domain"] = [],
        ["Whetstone.Application"] = ["Whetstone.Domain"],
        ["Whetstone.Infrastructure"] = ["Whetstone.Application"],
        ["Whetstone.Web"] =
        [
            "Whetstone.Application",
            "Whetstone.Infrastructure",
            "Whetstone.ServiceDefaults",
            "Whetstone.Web.Client",
        ],
        ["Whetstone.Web.Client"] = [],
        ["Whetstone.Worker"] =
        [
            "Whetstone.Application",
            "Whetstone.Infrastructure",
            "Whetstone.ServiceDefaults",
        ],
    };

    [Fact]
    public void Declared_project_references_match_the_5_1_table()
    {
        var violations = new List<string>();

        foreach ((string project, string[] allowed) in Allowed)
        {
            var csproj = Path.Combine(RepoRoot(), "src", project, project + ".csproj");
            Assert.True(File.Exists(csproj), $"expected {csproj} to exist");

            violations.AddRange(
                DeclaredReferences(csproj)
                    .Except(allowed, StringComparer.Ordinal)
                    .Select(bad => $"{project} declares a ProjectReference to {bad}"));
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// The table above governs only the projects it lists, so a project added to src/ and named
    /// in neither the table nor the exemption list would be governed by nothing at all. This is
    /// the tripwire that makes that impossible: a new project fails the build until someone
    /// decides, in a diff, which of the two it is.
    /// </summary>
    [Fact]
    public void Every_project_is_either_governed_or_deliberately_exempt()
    {
        var ungoverned = Directory
            .EnumerateDirectories(Path.Combine(RepoRoot(), "src"))
            // A directory only counts as a project if it holds one, so a stray folder under src/
            // is not a build failure — a new .csproj is.
            .Where(directory => Directory.EnumerateFiles(directory, "*.csproj").Any())
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(project => !Allowed.ContainsKey(project))
            .Where(project => !SolutionArchitecture.ExemptProjects.Contains(project, StringComparer.Ordinal))
            .ToArray();

        Assert.True(
            ungoverned.Length == 0,
            "these projects are governed by neither the 5.1 table nor the exemption list: "
            + string.Join(", ", ungoverned));
    }

    private static IEnumerable<string> DeclaredReferences(string csproj) =>
        XDocument.Load(csproj)
            .Descendants("ProjectReference")
            .Select(element => (string?)element.Attribute("Include") ?? string.Empty)
            // csproj paths use backslashes. On macOS and Linux a backslash is a legal filename
            // character rather than a separator, so GetFileNameWithoutExtension would hand back
            // the whole path and every comparison below would silently match nothing.
            .Select(include => Path.GetFileNameWithoutExtension(include.Replace('\\', '/')))
            .Where(name => !string.IsNullOrEmpty(name));

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Whetstone.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException($"Whetstone.slnx not found above {AppContext.BaseDirectory}");
    }
}
