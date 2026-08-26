using static ArchUnitNET.Fluent.ArchRuleDefinition;

using ArchUnitNET.Domain;

using Whetstone.Application;

namespace Whetstone.Architecture.Tests;

public class WithholdingBoundaryTests
{
    [Fact(Skip = "Lands in issue #58")]
    public void NoGradedPayloadCanCarryTheWithheldHalf()
    {
        Class[] roots = Classes().That()
            .HaveAnyAttributes(typeof(GradedPayloadAttribute))
            .GetObjects(SolutionArchitecture.Instance)
            .ToArray();

        Assert.NotEmpty(roots);

        string[] leaks = roots
            .Select(root => new { root, path = PathToWithheldHalf(root) })
            .Where(x => x.path is not null)
            .Select(x => $"{x.root.Name} reaches the withheld half via {x.path}")
            .ToArray();

        Assert.True(leaks.Length == 0, string.Join(Environment.NewLine, leaks));
    }

    /// <summary>
    /// Returns the member path by which the withheld half is reachable from <paramref name="root"/> —
    /// e.g. "GradingRequest.Exercise.Candidate.Finding" — or null when it is not reachable.
    ///
    /// "The withheld half" means any member, transitively reachable through member types, that is
    /// ExerciseCandidate, exposes an ExerciseCandidate, or is named Finding or Rationale.
    /// </summary>
    private static string? PathToWithheldHalf(IType root)
    {
        // Yours to write. See the decision list below.
        throw new NotImplementedException();
    }
}
