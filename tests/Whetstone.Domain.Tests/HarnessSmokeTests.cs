using FsCheck.Xunit;

namespace Whetstone.Domain.Tests;

/// <summary>
/// Ticket 0.3 wires FsCheck into this project. A package reference alone does not
/// prove the xUnit v2 runner discovers and executes a [Property] — this test does,
/// and fails if a version bump breaks that pairing.
/// </summary>
public class HarnessSmokeTests
{
    [Property]
    public bool Reversing_a_sequence_twice_is_the_identity(int[] xs) =>
        xs.Reverse().Reverse().SequenceEqual(xs);
}
