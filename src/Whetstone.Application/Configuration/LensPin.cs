namespace Whetstone.Application.Configuration;

/// <summary>
/// One registered Lens, pinned: <c>id@version</c> plus the content hash recorded at
/// registration (ADR-0007 §1). Resolution is by hash; upstream editing the same version is a
/// hash mismatch and fails the generation run, never a silent update.
/// </summary>
public sealed class LensPin
{
    public string? Id { get; set; }

    public string? Version { get; set; }

    public string? ContentHash { get; set; }

    /// <summary>
    /// Every field with the configuration key it binds from, for validation messages that name
    /// the key (ticket 0.7: name the key, never echo the value).
    /// </summary>
    internal IEnumerable<(string Key, string? Value)> RequiredFields() =>
        [(nameof(Id), Id), (nameof(Version), Version), (nameof(ContentHash), ContentHash)];
}
