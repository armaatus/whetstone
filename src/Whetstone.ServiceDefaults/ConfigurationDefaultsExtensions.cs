using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

// Same namespace convention as ServiceDefaultsExtensions: shared host wiring that every
// composition root picks up without an import.
namespace Microsoft.Extensions.Hosting;

/// <summary>
/// The configuration half of the shared host wiring (spec 13.5, ticket 0.7), consumed by each
/// host's OptionsWiring. Lives here because both hosts need the identical shape and two copies
/// of it would drift.
/// </summary>
public static class ConfigurationDefaultsExtensions
{
    /// <summary>
    /// One validated options registration: bind by section path, validate annotations, and
    /// fail at boot. Always all three — <c>ValidateDataAnnotations</c> alone validates lazily
    /// on first resolution, which for a background job can be hours after boot on a
    /// healthy-looking host (spec 13.5).
    /// </summary>
    public static OptionsBuilder<TOptions> AddValidatedOptions<TOptions>(
        this IHostApplicationBuilder builder, string sectionPath)
        where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.Services.AddOptions<TOptions>()
            .BindConfiguration(sectionPath)
            .ValidateDataAnnotations()
            .ValidateOnStart();
    }

    /// <summary>
    /// Makes spec 13.5's "nothing else in the layering — no JSON file per environment" true at
    /// runtime, not just in the repo tree: the default host pipeline loads
    /// appsettings.{Environment}.json when one sits beside the binary, so a stray
    /// appsettings.Production.json on a server would quietly join the layering. This strips
    /// every appsettings.*.json source except appsettings.json itself. User secrets survive —
    /// their provider reads secrets.json, which the prefix check does not match.
    /// </summary>
    public static TBuilder EnforceConfigurationLayering<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        IList<IConfigurationSource> sources = ((IConfigurationBuilder)builder.Configuration).Sources;

        for (int i = sources.Count - 1; i >= 0; i--)
        {
            if (sources[i] is JsonConfigurationSource { Path: { } path }
                && path.StartsWith("appsettings.", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(path, "appsettings.json", StringComparison.OrdinalIgnoreCase))
            {
                sources.RemoveAt(i);
            }
        }

        return builder;
    }
}
