using NSubstitute;

namespace Whetstone.Application.Tests;

/// <summary>
/// Ticket 0.3 wires NSubstitute into this project. This test proves the proxy
/// generator actually runs on this target framework, rather than only that the
/// package restores.
/// </summary>
public class HarnessSmokeTests
{
    [Fact]
    public void A_substitute_returns_its_configured_value()
    {
        var comparer = Substitute.For<IComparer<int>>();
        comparer.Compare(1, 2).Returns(42);

        Assert.Equal(42, comparer.Compare(1, 2));
    }
}
