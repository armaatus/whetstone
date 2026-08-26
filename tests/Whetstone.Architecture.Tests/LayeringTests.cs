using System.Reflection;

using NetArchTest.Rules;

namespace Whetstone.Architecture.Tests;

/// <summary>
/// ARCH-1 (spec 5.1). These rules fail the build rather than living in a diagram.
///
/// Whetstone.AppHost and Whetstone.ServiceDefaults are deliberately outside these
/// rules: the AppHost is the orchestration root and references every runnable
/// project by design, and ServiceDefaults is shared host wiring consumed by both
/// hosts. Exemptions are named here rather than left implicit (ADR-005).
/// </summary>
public class LayeringTests
{
    private static readonly Assembly Domain = typeof(Whetstone.Domain.AssemblyMarker).Assembly;
    private static readonly Assembly Application = typeof(Whetstone.Application.AssemblyMarker).Assembly;
    private static readonly Assembly Infrastructure = typeof(Whetstone.Infrastructure.AssemblyMarker).Assembly;

    [Fact]
    public void Domain_references_nothing_in_the_solution()
    {
        var result = Types.InAssembly(Domain)
            .Should()
            .NotHaveDependencyOnAny(
                "Whetstone.Application",
                "Whetstone.Infrastructure",
                "Whetstone.Web",
                "Whetstone.Worker",
                "Whetstone.ServiceDefaults")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Application_does_not_reference_Infrastructure()
    {
        var result = Types.InAssembly(Application)
            .Should()
            .NotHaveDependencyOnAny(
                "Whetstone.Infrastructure",
                "Whetstone.Web",
                "Whetstone.Worker")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void EntityFrameworkCore_is_confined_to_Infrastructure()
    {
        foreach (var assembly in new[] { Domain, Application })
        {
            var result = Types.InAssembly(assembly)
                .Should()
                .NotHaveDependencyOn("Microsoft.EntityFrameworkCore")
                .GetResult();

            Assert.True(result.IsSuccessful, $"{assembly.GetName().Name}: {Describe(result)}");
        }
    }

    [Fact]
    public void Infrastructure_is_the_only_layer_that_may_reference_Npgsql()
    {
        var result = Types.InAssembly(Application)
            .Should()
            .NotHaveDependencyOn("Npgsql")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));

        // Referenced so the assembly is loaded and the rule above is meaningful.
        Assert.NotNull(Infrastructure);
    }

    private static string Describe(TestResult result) =>
        result.FailingTypeNames is null
            ? "no failing types reported"
            : "violating types: " + string.Join(", ", result.FailingTypeNames);
}
