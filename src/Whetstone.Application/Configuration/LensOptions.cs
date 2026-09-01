using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace Whetstone.Application.Configuration;

/// <summary>
/// The Lens registry: where registered Lenses come from, and the <c>id@version</c> + content
/// hash each one is pinned at (ADR-0007 §1 — resolution is by hash, and a mismatch fails the
/// run loudly rather than falling back to newest).
///
/// Deliberately absent: any notion of <em>enablement</em>. A Lens is enabled per tenant by a
/// <c>TenantAdmin</c>, recorded with who, when, at which hash and what corpus scope, and every
/// change writes an <c>audit_event</c> (ADR-0007 §2). That is database state; a key shaped like
/// <c>EnabledForAllTenants</c> must never appear here.
///
/// An empty registry with no pins is a valid state — the Lens pipeline arrives with Epic 2.5 —
/// so unlike the AI and corpus secrets nothing here is unconditionally required. What is
/// enforced: a pin without a complete <c>id@version</c> + hash is a boot failure, because an
/// unpinned Lens is exactly what ADR-0007 §1 forbids.
/// </summary>
public sealed class LensOptions : IValidatableObject
{
    public const string SectionName = "Whetstone:Lens";

    public string? Registry { get; set; }

    /// <summary>
    /// Secret, but optional: a public registry needs none. When set it follows every rule the
    /// other secrets do — user secrets in dev, environment variable in production, never logged.
    /// </summary>
    public string? RegistryApiKey { get; set; }

    public IList<LensPin> Pins { get; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Pins.Count > 0 && string.IsNullOrWhiteSpace(Registry))
        {
            yield return new ValidationResult(
                "Whetstone:Lens:Registry is not set, but Lens pins are configured. Pins are resolved against the registry, so name it — or remove the pins.",
                [nameof(Registry)]);
        }

        for (int i = 0; i < Pins.Count; i++)
        {
            foreach ((string key, string? value) in Pins[i].RequiredFields())
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    yield return new ValidationResult(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"Whetstone:Lens:Pins:{i}:{key} is not set. A Lens is registered as id@version with a content hash recorded at registration (ADR-0007 §1); a partial pin is not a pin."),
                        [nameof(Pins)]);
                }
            }
        }
    }
}
