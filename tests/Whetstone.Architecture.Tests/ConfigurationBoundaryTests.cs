using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Whetstone.Architecture.Tests;

/// <summary>
/// Ticket 0.7: IConfiguration stops at the composition root. Everything past it takes a typed,
/// validated <c>IOptions&lt;T&gt;</c> — a class that reads IConfiguration directly has opted
/// out of <c>ValidateOnStart</c>, and its missing key is a 3am exception again.
/// </summary>
public class ConfigurationBoundaryTests
{
    /// <summary>
    /// The configuration surface itself — the interfaces plus ConfigurationManager — rather
    /// than the whole Microsoft.Extensions.Configuration namespace: the binder's extension
    /// classes live in namespaces the composition-root wiring legitimately touches, and it is
    /// holding a configuration <em>object</em> that this rule exists to forbid.
    /// </summary>
    private const string ConfigurationSurface =
        @"^Microsoft\.Extensions\.Configuration\.I?Configuration(Root|Section|Manager|Builder)?$";

    /// <summary>
    /// Subjects are matched by namespace, which is what exempts the composition roots: a
    /// top-level Program lives in the global namespace, while every declared type in the
    /// solution lives under Whetstone.*. The per-host OptionsWiring classes pass on merit
    /// rather than exemption — they bind by section path and never hold a configuration object.
    ///
    /// Stated plainly, the exemption is exactly as wide as it is: ANY global-namespace type
    /// escapes, not only Program — the convention this rule encodes is "global namespace =
    /// composition-root territory". And like every rule in this project it governs only the
    /// assemblies in <see cref="SolutionArchitecture"/>'s graph, so ServiceDefaults and AppHost
    /// (host wiring by definition — see ExemptProjects) may hold configuration objects freely.
    /// </summary>
    [Fact]
    public void IConfiguration_reaches_nothing_outside_the_composition_roots() =>
        SolutionArchitecture.Enforce(
            Types().That().ResideInNamespaceMatching(@"^Whetstone\.")
                .Should().NotDependOnAnyTypesThat().HaveFullNameMatching(ConfigurationSurface)
                .Because("ticket 0.7: configuration is read once, at the composition root, into options validated at startup — everything else takes IOptions<T>"));
}
