using System.ComponentModel.DataAnnotations;

namespace Whetstone.Application.Configuration;

/// <summary>
/// Platform-level access to the tenant's Git corpus: the host the fetcher clones from and the
/// credential it presents. Which repository and what scope a given tenant registered is tenant
/// data, not configuration (ticket 0.7); this section holds only what the platform needs before
/// any tenant exists.
///
/// There is no ingestion-mode key. The ticket sketched one while OQ-9 was open; ADR-0011 closed
/// OQ-9 as clone-and-index, so a mode setting would present a settled decision as configurable.
///
/// The token is the highest-value secret in the system — the AI key buys inference, this buys
/// the customer's source code (ADR-0006, ADR-0011 §4). It is therefore governed exactly like
/// the AI key: user secrets in dev, environment variable in production, validated at boot,
/// never logged — and held by the Worker only, because the fetcher is the only process that
/// needs it (ADR-0011 §4).
/// </summary>
public sealed class CorpusOptions
{
    public const string SectionName = "Whetstone:Corpus";

    public string? Host { get; set; }

    /// <summary>
    /// Read-only, repository-scoped (ADR-0011 §4). Required at boot: a system that starts
    /// without corpus access looks healthy and produces zero candidates forever, which is the
    /// failure mode <c>ValidateOnStart</c> exists to prevent.
    /// </summary>
    [Required(ErrorMessage = "Whetstone:Corpus:RepoAccessToken is not set, and without it the corpus fetcher cannot reach any tenant repository. Development: dotnet user-secrets set \"Whetstone:Corpus:RepoAccessToken\" <value>. Production: the Whetstone__Corpus__RepoAccessToken environment variable.")]
    public string? RepoAccessToken { get; set; }
}
