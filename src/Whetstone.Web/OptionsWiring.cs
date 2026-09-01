using Whetstone.Application.Configuration;

namespace Whetstone.Web;

/// <summary>
/// Composition-root half of ticket 0.7, Web edition: the database section and nothing else.
///
/// The other three sections are deliberately absent. Grading and fetching are Worker jobs
/// (spec §5.3), and ADR-0011 §4 keeps the corpus token out of every process but the fetcher —
/// a section this host binds is a secret its environment must carry, so not binding is the
/// enforcement. When Epic 4 surfaces provider and model in the UI (ticket 4.15), it can bind a
/// display-only subset here; the key stays with the Worker.
///
/// <c>AddValidatedOptions</c> (ServiceDefaults) is bind-by-path + <c>ValidateDataAnnotations</c>
/// + <c>ValidateOnStart</c>, so a missing connection string fails the deploy at boot, not the
/// first query (spec §13.5).
/// </summary>
internal static class OptionsWiring
{
    public static IHostApplicationBuilder AddWhetstoneOptions(this IHostApplicationBuilder builder)
    {
        // Spec 13.5's layering holds at runtime too: no appsettings.{Environment}.json, even
        // one placed beside the binary after deployment.
        builder.EnforceConfigurationLayering();

        builder.AddValidatedOptions<DatabaseOptions>(DatabaseOptions.SectionName);

        return builder;
    }
}
