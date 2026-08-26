using ArchUnitNET.Fluent.Syntax.Elements.Types.Interfaces;

using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Whetstone.Architecture.Tests;

/// <summary>
/// ADR-0006 §1, as executable rules.
///
/// > The port lives in `Application`; every adapter lives in `Infrastructure`. `Domain` knows
/// > about `ExerciseCandidate` and about withholding; it knows nothing about Git, GitHub, or
/// > Lenses.
///
/// The first two rules are written before the types they govern exist, so the shape is fixed
/// before the first adapter is placed rather than audited after the fifth.
/// </summary>
public class ExerciseSourceLayeringTests
{
    /// <summary>
    /// The port, resolved by name so this compiles before <c>IExerciseSource</c> exists.
    ///
    /// An expression-bodied property rather than a static readonly field: ArchUnitNET's fluent
    /// objects are mutable builders, and sharing one instance across two rules leaves the second
    /// rule carrying the first one's condition. Each access must build a fresh one.
    /// </summary>
    private static GivenInterfacesConjunction Port =>
        Interfaces().That().HaveName("IExerciseSource");

    /// <summary>
    /// ADR-0006 §1 puts the port in Application. Asserting on every interface of that name — rather
    /// than looking one up in Application — is what also catches the copy-paste duplicate in Web.
    ///
    /// There is no separate "the interface exists" assertion because there does not need to be:
    /// with the port absent, ArchUnitNET fails this rule with "requires positive evaluation".
    /// Un-skipping it early is loud, not silently green.
    ///
    /// It does not enforce *exactly* one — two interfaces named IExerciseSource in different
    /// Application namespaces would pass. That gap is narrow enough to accept knowingly.
    /// </summary>
    [Fact(Skip = "IExerciseSource lands in #104 (S1 — IExerciseSource port). Un-skipping this is an acceptance criterion there.")]
    public void IExerciseSource_is_declared_in_Application_and_nowhere_else() =>
        SolutionArchitecture.Enforce(
            Port.Should().ResideInAssemblyMatching(SolutionArchitecture.ApplicationAssembly)
                .Because("ADR-0006 §1: IExerciseSource is a port, and ports live in Whetstone.Application"));

    /// <summary>
    /// The rule discovers implementors rather than listing them, so a sixth adapter added to the
    /// wrong project fails without anyone updating this test.
    ///
    /// Note the query starts from every assembly, not from Infrastructure. "Types in Infrastructure
    /// implementing the port should be in Infrastructure" is a tautology — an adapter in
    /// Application was never in the source set, so it could not fail. The assertion is about
    /// *where each implementor was found*, which inverts it relative to instinct.
    /// </summary>
    [Fact(Skip = "adapters land in #106 (S3 — GitHistorySource). Un-skipping this is an acceptance criterion there.")]
    public void Every_IExerciseSource_implementation_is_declared_in_Infrastructure() =>
        SolutionArchitecture.Enforce(
            Classes().That().AreAssignableTo(Port)
                .Should().ResideInAssemblyMatching(SolutionArchitecture.InfrastructureAssembly)
                .Because("ADR-0006 §1: GitHistorySource, PullRequestSource, AdrSource, LensSource and ManualSource are all Infrastructure"));

    /// <summary>
    /// The Domain models candidates and withholding; *acquiring* candidates is an Infrastructure
    /// concern behind the port. Application is in scope too, because a port that leaks HttpClient
    /// into its signature is not a port.
    ///
    /// System.Net.Http is deliberately blunt — it catches HttpClient, HttpRequestMessage and the
    /// whole namespace, because a Domain type holding any of them is wrong for the same reason.
    /// </summary>
    [Fact]
    public void Domain_and_Application_know_nothing_about_Git_GitHub_or_Lenses() =>
        SolutionArchitecture.Enforce(
            Types().That().ResideInAssemblyMatching(DomainAndApplication)
                .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching(SourceClientSurface)
                .Because("ADR-0006 §1: Domain knows nothing about Git, GitHub, or Lenses"));

    private const string DomainAndApplication = @"^Whetstone\.(Domain|Application),";

    /// <summary>
    /// Known incomplete, and recorded as such rather than left to look finished.
    ///
    /// No Git or GitHub client library is chosen yet — ADR-0011 settles on clone-and-index but
    /// names no package — so these three are a plausible surface, not a decided one. Revisit when
    /// #106 (S3 — GitHistorySource) picks one.
    ///
    /// **There is deliberately no Lens entry, and there may never be one.** ADR-0007 makes a Lens
    /// *pinned content* — instructions resolved by hash and fed to a model in a separate generation
    /// process — not a library this solution links against. So "Domain knows nothing about Lenses"
    /// has no package namespace to forbid; it is enforced instead by the layering rules, which keep
    /// Domain and Application away from the Infrastructure adapter that runs them. This rule's name
    /// promises more than a namespace check can deliver, and that is the honest reading of it.
    /// </summary>
    private const string SourceClientSurface = @"^(LibGit2Sharp|Octokit|System\.Net\.Http)";
}
