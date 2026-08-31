using System.ComponentModel.DataAnnotations;

namespace Whetstone.Application.Configuration;

/// <summary>
/// The AI provider the grading pipeline calls through <c>IChatClient</c> (spec §9, ticket 4.1).
///
/// Only the key is validated at startup: which provider and model — and therefore what
/// non-empty values belong in the other properties — is OQ-2, open until Epic 4. The key is
/// required <em>now</em> because ticket 0.7's contract is that a missing secret is a boot
/// failure, not a 3am surprise on the first graded turn.
/// </summary>
public sealed class AiOptions
{
    public const string SectionName = "Whetstone:Ai";

    public string? Provider { get; set; }

    public string? Model { get; set; }

    public string? Endpoint { get; set; }

    /// <summary>
    /// Secret. Dev: user secrets. Prod: environment variable. Never a JSON file in the repo
    /// tree (spec §13.5) — and never logged (spec §7.8).
    /// </summary>
    [Required(ErrorMessage = "Whetstone:Ai:ApiKey is not set. Development: dotnet user-secrets set \"Whetstone:Ai:ApiKey\" <value>. Production: the Whetstone__Ai__ApiKey environment variable.")]
    public string? ApiKey { get; set; }
}
