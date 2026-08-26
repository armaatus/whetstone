using Bunit;

namespace Whetstone.Web.Tests;

/// <summary>
/// Ticket 0.3 wires bUnit into this project. bUnit 2.x carries no test-framework
/// dependency of its own, so nothing but an actual render proves it works with the
/// xUnit v2 runner this repo pins.
/// </summary>
public class HarnessSmokeTests
{
    [Fact]
    public void Bunit_renders_a_component_fragment()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render(builder => builder.AddMarkupContent(0, "<p>ok</p>"));

        cut.MarkupMatches("<p>ok</p>");
    }
}
