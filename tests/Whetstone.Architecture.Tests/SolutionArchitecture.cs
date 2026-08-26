using System.Reflection;
using System.Text.RegularExpressions;

using ArchUnitNET.Fluent;
using ArchUnitNET.Loader;

// `Architecture` on its own binds to the enclosing `Whetstone.Architecture` namespace,
// not to ArchUnitNET's type. The alias is what makes the type nameable in this project.
using ArchUnitArchitecture = ArchUnitNET.Domain.Architecture;

namespace Whetstone.Architecture.Tests;

/// <summary>
/// The loaded type graph every rule in this project queries, plus the assertion that turns
/// a rule into an xUnit failure.
///
/// Building the graph costs roughly half a second, so it is built exactly once per test run
/// and shared. ArchUnitNET reads assemblies from disk via Mono.Cecil rather than through the
/// CLR loader, but the AssemblyMarker types are still what names them: passing a marker's
/// assembly is how a project gets into the graph, and a project absent from the graph is a
/// project no rule can govern.
/// </summary>
internal static partial class SolutionArchitecture
{
    private static readonly Assembly[] Layers =
    [
        typeof(Whetstone.Domain.AssemblyMarker).Assembly,
        typeof(Whetstone.Application.AssemblyMarker).Assembly,
        typeof(Whetstone.Infrastructure.AssemblyMarker).Assembly,
        typeof(Whetstone.Web.AssemblyMarker).Assembly,
        typeof(Whetstone.Worker.AssemblyMarker).Assembly,
        typeof(Whetstone.Web.Client.AssemblyMarker).Assembly,
    ];

    /// <summary>
    /// Assembly-membership regexes, matched against an assembly's *full* name — which is
    /// "Whetstone.Domain, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null", not the bare
    /// project name.
    ///
    /// The trailing comma is load-bearing: without it "^Whetstone\.Web" also matches
    /// Whetstone.Web.Client, which spec 5.1 governs by a different rule.
    ///
    /// ResideInAssembly(string) is deliberately not used anywhere in this project — it compares
    /// against that whole identity, so a bare project name silently selects nothing.
    ///
    /// These live here rather than on one test class because more than one class needs them and
    /// two copies would drift.
    /// </summary>
    public const string DomainAssembly = @"^Whetstone\.Domain,";
    public const string ApplicationAssembly = @"^Whetstone\.Application,";
    public const string InfrastructureAssembly = @"^Whetstone\.Infrastructure,";
    public const string WebClientAssembly = @"^Whetstone\.Web\.Client,";

    /// <summary>
    /// Spec 5.1 says "any type outside Whetstone.Infrastructure", so Web, Web.Client and Worker
    /// are all in scope for the persistence rule even though Web and Worker may reference
    /// Infrastructure itself.
    ///
    /// "Web" and "Web\.Client" are separate alternatives on purpose: the trailing comma anchors
    /// each on the end of a simple assembly name, so "Web" alone would not match Web.Client.
    /// </summary>
    public const string OutsideInfrastructure = @"^Whetstone\.(Domain|Application|Web|Web\.Client|Worker),";

    /// <summary>
    /// The projects ARCH-1 does not govern, named rather than left implicit.
    ///
    /// Whetstone.AppHost is the Aspire orchestration root: it references every runnable project
    /// by design, so a "references nothing above it" rule is meaningless there (ADR-0005).
    ///
    /// Whetstone.ServiceDefaults is shared host wiring (telemetry, health checks, resilience)
    /// consumed by both hosts. It sits beside the layers rather than inside them.
    ///
    /// Single-sourced here because two tests depend on it: <see cref="ExemptionTests"/> pins the
    /// length so the list cannot grow quietly, and <see cref="ProjectReferenceTests"/> uses it to
    /// decide which projects its table is allowed to omit.
    /// </summary>
    public static readonly string[] ExemptProjects =
    [
        "Whetstone.AppHost",
        "Whetstone.ServiceDefaults",
    ];

    public static readonly ArchUnitArchitecture Instance = new ArchLoader()
        .LoadAssemblies(Layers)
        .Build();

    /// <summary>
    /// Evaluates <paramref name="rule"/> and fails the test with every violation named.
    ///
    /// ArchUnitNET reports a violation for a rule whose subject set is empty ("the rule requires
    /// positive evaluation"), so a rule that has quietly stopped matching anything fails here
    /// rather than passing vacuously. That default is the reason this project uses ArchUnitNET
    /// and not an IL query library that treats "matched nothing" as success — and it is what
    /// catches a layer regex below that no longer matches the assembly it was written for.
    /// </summary>
    public static void Enforce(IArchRule rule)
    {
        var violations = rule.Evaluate(Instance)
            .Where(result => !result.Passed)
            .Select(result => "  " + Readable(result.Description))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            Readable(rule.ToString() ?? "<rule>") + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// ArchUnitNET names assemblies by full identity, so messages carry a
    /// ", Version=1.0.0.0, Culture=neutral, PublicKeyToken=null" tail that is identical on every
    /// line and tells a reader nothing. Strip it so the failure reads as a sentence.
    /// </summary>
    private static string Readable(string description) =>
        AssemblyIdentityTail().Replace(description, string.Empty);

    [GeneratedRegex(@", Version=[\d.]+, Culture=[\w-]+, PublicKeyToken=\w+")]
    private static partial Regex AssemblyIdentityTail();
}
