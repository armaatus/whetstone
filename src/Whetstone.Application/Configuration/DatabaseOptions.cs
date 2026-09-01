using System.ComponentModel.DataAnnotations;

namespace Whetstone.Application.Configuration;

/// <summary>
/// The application's database connection — as <c>whetstone_app</c>, the least-privileged of the
/// three roles from spec §7.3 (ticket 0.6). The migrator connection is deliberately not here:
/// migrations run as a separate one-shot step under a different role (spec §6.5), and an option
/// the app binds is an option the app can use.
/// </summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Whetstone:Database";

    /// <summary>
    /// Secret — it carries the role's password. In development the AppHost injects it
    /// (<c>Whetstone__Database__ConnectionString</c>); production sets the same variable.
    /// </summary>
    [Required(ErrorMessage = "Whetstone:Database:ConnectionString is not set. Development: run via the AppHost (dotnet aspire run), which injects it. Production: the Whetstone__Database__ConnectionString environment variable, connecting as whetstone_app.")]
    public string? ConnectionString { get; set; }
}
