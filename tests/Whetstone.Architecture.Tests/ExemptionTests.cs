namespace Whetstone.Architecture.Tests;

/// <summary>
/// The exemption list is a fact in the build, not a habit. Adding a third exemption requires
/// editing this assertion, which shows up in a diff and demands a sentence in the PR.
///
/// The list itself lives on <see cref="SolutionArchitecture.ExemptProjects"/>, where the reason
/// for each entry is recorded and where <see cref="ProjectReferenceTests"/> can reach it.
/// </summary>
public class ExemptionTests
{
    [Fact]
    public void The_exemption_list_has_not_grown() =>
        Assert.Equal(
            ["Whetstone.AppHost", "Whetstone.ServiceDefaults"],
            SolutionArchitecture.ExemptProjects);
}
