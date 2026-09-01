// PROOF ARTEFACT for ticket 0.8 — planted deliberately, deleted before this PR merges.
// Both values below are SYNTHETIC: hand-typed literals, never issued by GitHub, granting
// nothing. This is the exact scenario the ticket names — a corpus repository access token
// (ADR-0006) pasted into a test file to "just check the call works".
namespace Whetstone.Worker.Tests;

internal static class CorpusTokenLeakProbe
{
    // Canonical shape: exactly 36 characters after the prefix. Upstream github-pat catches this.
    public const string RepoAccessToken = "ghp_wh3tst0neF4keC0rpusT0kenNotReal12345";

    // Two characters short of canonical. Invisible to EVERY upstream github-* rule, all of which
    // pin {36} exactly. This is what whetstone-corpus-github-token exists for.
    public const string RetypedToken = "ghp_wh3tst0neF4keC0rpusT0kenNotReal123";
}
