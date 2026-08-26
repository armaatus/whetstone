using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Whetstone.Architecture.Tests;

/// <summary>
/// Spec 5.1, as executable rules: an architecture rule that is not executable is a diagram,
/// and diagrams do not fail builds (spec 13.7).
///
/// Whetstone.AppHost and Whetstone.ServiceDefaults are deliberately outside these rules: the
/// AppHost is the orchestration root and references every runnable project by design, and
/// ServiceDefaults is shared host wiring consumed by both hosts. Exemptions are named in
/// <see cref="ExemptionTests"/> rather than left implicit (ADR-0005).
/// </summary>
public class LayeringTests
{
    // Assembly-membership regexes live on SolutionArchitecture: more than one test class needs
    // them, and the comma anchor is easy to get wrong twice.
    //
    // Layers are matched by *namespace* on the dependency side, because a dependency on a project
    // that is not itself loaded into the graph — ServiceDefaults, for one — still resolves to a
    // type whose namespace names the layer it came from.
    private const string AboveDomain = @"^Whetstone\.(Application|Infrastructure|Web|Worker|ServiceDefaults)";
    private const string AboveApplication = @"^Whetstone\.(Infrastructure|Web|Worker)";

    /// <summary>
    /// Prefixed rather than exact so Npgsql.EntityFrameworkCore.PostgreSQL and every
    /// Microsoft.EntityFrameworkCore.* namespace are caught by the same rule.
    /// </summary>
    private const string PersistenceSurface = @"^(Microsoft\.EntityFrameworkCore|Npgsql)";

    [Fact]
    public void Domain_references_nothing_in_the_solution() =>
        SolutionArchitecture.Enforce(
            Types().That().ResideInAssemblyMatching(SolutionArchitecture.DomainAssembly)
                .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching(AboveDomain)
                .Because("spec 5.1 puts Domain at the bottom: everything may depend on it, and it depends on none of them"));

    [Fact]
    public void Application_does_not_reference_Infrastructure() =>
        SolutionArchitecture.Enforce(
            Types().That().ResideInAssemblyMatching(SolutionArchitecture.ApplicationAssembly)
                .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching(AboveApplication)
                .Because("spec 5.1: Application depends on Domain only. Infrastructure implements Application's ports, never the reverse"));

    /// <summary>
    /// Spec 5.1: "Whetstone.Web.Client → WASM components. No server-side refs."
    ///
    /// Everything the server owns is off limits, not just the obvious layers: Web.Client is
    /// downloaded to and executed in a browser, so a server-side type reaching it is either a
    /// build break or — worse, if it links — server code shipped to every visitor. ServiceDefaults
    /// is included despite being exempt from the layering rules, because it is host wiring and a
    /// WASM client is not a host.
    ///
    /// Whetstone.Web itself is the server half of the same Blazor app; the reference goes
    /// Web → Web.Client and never back.
    /// </summary>
    [Fact]
    public void Web_Client_takes_no_server_side_references() =>
        SolutionArchitecture.Enforce(
            Types().That().ResideInAssemblyMatching(SolutionArchitecture.WebClientAssembly)
                .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching(EverythingElseInTheSolution)
                .Because("spec 5.1: Whetstone.Web.Client runs in the browser and takes no server-side refs"));

    /// <summary>
    /// "Server-side" here means every other Whetstone project, including Domain. That is stronger
    /// than the phrase suggests, and it is what 5.1 actually says: the table gives Web.Client an
    /// empty reference list, which <see cref="ProjectReferenceTests"/> already enforces at the
    /// declared level. This is the same fact at the IL level, so the two cannot disagree — sharing
    /// a DTO with the browser becomes a decision made in one diff, in two places, or not at all.
    ///
    /// The negative lookahead is load-bearing: Whetstone.Web.Client is itself nested under
    /// Whetstone.Web, so a plain "^Whetstone\." would flag the client's own types and the rule
    /// would fail the moment Web.Client had two types that referenced each other.
    /// </summary>
    private const string EverythingElseInTheSolution = @"^Whetstone\.(?!Web\.Client)";

    /// <summary>
    /// Spec 5.1 says "any type outside Whetstone.Infrastructure", so Web and Worker are in scope
    /// even though they are allowed to reference Infrastructure itself. Checking only Domain and
    /// Application — as this rule did before — leaves the hole exactly where the composition
    /// root sits.
    ///
    /// This subsumes the separate Npgsql-only rule that used to sit beside it: Npgsql is already
    /// in the surface below, and two rules asserting one thing means one of them rots unnoticed.
    /// </summary>
    [Fact]
    public void Persistence_packages_are_confined_to_Infrastructure() =>
        SolutionArchitecture.Enforce(
            Types().That().ResideInAssemblyMatching(SolutionArchitecture.OutsideInfrastructure)
                .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching(PersistenceSurface)
                .Because("spec 5.1: EF Core and Npgsql are Infrastructure's to own. A composition root that names an EF type has already leaked it"));
}
