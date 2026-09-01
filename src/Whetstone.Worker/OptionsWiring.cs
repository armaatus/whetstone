using Whetstone.Application.Configuration;

namespace Whetstone.Worker;

/// <summary>
/// Composition-root half of ticket 0.7. The Worker binds every section, because every consumer
/// of a secret is a job: grading calls the AI provider (spec §5.3), <c>FetchCorpus</c> is the
/// only process allowed to hold the corpus token (ADR-0011 §4), and generation resolves Lenses
/// by pinned hash (ADR-0007 §1).
///
/// Each registration goes through <c>AddValidatedOptions</c> (ServiceDefaults), which is
/// bind-by-path + <c>ValidateDataAnnotations</c> + <c>ValidateOnStart</c>, always (spec §13.5).
/// Binding is by section <em>path</em>, so nothing here — and nothing anywhere outside the
/// composition roots, as ConfigurationBoundaryTests asserts — takes an IConfiguration.
/// </summary>
internal static class OptionsWiring
{
    public static IHostApplicationBuilder AddWhetstoneOptions(this IHostApplicationBuilder builder)
    {
        // Spec 13.5's layering holds at runtime too: no appsettings.{Environment}.json, even
        // one placed beside the binary after deployment.
        builder.EnforceConfigurationLayering();

        builder.AddValidatedOptions<AiOptions>(AiOptions.SectionName);
        builder.AddValidatedOptions<CorpusOptions>(CorpusOptions.SectionName);
        builder.AddValidatedOptions<LensOptions>(LensOptions.SectionName);
        builder.AddValidatedOptions<DatabaseOptions>(DatabaseOptions.SectionName);

        return builder;
    }
}
