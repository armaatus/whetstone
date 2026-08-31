using Whetstone.Application.Configuration;

namespace Whetstone.Worker;

/// <summary>
/// Composition-root half of ticket 0.7. The Worker binds every section, because every consumer
/// of a secret is a job: grading calls the AI provider (spec §5.3), <c>FetchCorpus</c> is the
/// only process allowed to hold the corpus token (ADR-0011 §4), and generation resolves Lenses
/// by pinned hash (ADR-0007 §1).
///
/// <c>ValidateOnStart</c> on every registration, not just <c>ValidateDataAnnotations</c>: the
/// latter alone validates lazily on first resolution, which for a background job can be hours
/// after boot on a healthy-looking host. Both calls, always (spec §13.5).
///
/// Binding is by section <em>path</em>, so nothing here — and nothing anywhere outside the
/// composition roots, as ConfigurationBoundaryTests asserts — takes an IConfiguration.
/// </summary>
internal static class OptionsWiring
{
    public static IHostApplicationBuilder AddWhetstoneOptions(this IHostApplicationBuilder builder)
    {
        builder.Services.AddOptions<AiOptions>()
            .BindConfiguration(AiOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services.AddOptions<CorpusOptions>()
            .BindConfiguration(CorpusOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services.AddOptions<LensOptions>()
            .BindConfiguration(LensOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services.AddOptions<DatabaseOptions>()
            .BindConfiguration(DatabaseOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return builder;
    }
}
