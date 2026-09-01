using Microsoft.Extensions.Hosting;

namespace Whetstone.Worker.Tests;

/// <summary>
/// Spec 13.5: "Nothing else in the layering — no config server, no JSON file per environment."
/// The repo-tree half is enforced by gitignore and AppSettingsStructureTests; this is the
/// runtime half. The default host pipeline loads appsettings.{Environment}.json when one sits
/// beside the binary, so a stray appsettings.Production.json dropped on a server would quietly
/// join the layering — unless the composition root strips it, which these tests pin.
/// </summary>
public sealed class ConfigurationLayeringTests : IDisposable
{
    private readonly string _contentRoot =
        Directory.CreateTempSubdirectory("whetstone-layering-").FullName;

    public ConfigurationLayeringTests() =>
        File.WriteAllText(
            Path.Combine(_contentRoot, "appsettings.Production.json"),
            /*lang=json*/ """{ "Whetstone": { "PlantedByTest": "leaked" } }""");

    public void Dispose() => Directory.Delete(_contentRoot, recursive: true);

    private HostApplicationBuilder ProductionBuilder() =>
        Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = _contentRoot,
            EnvironmentName = Environments.Production,
        });

    /// <summary>
    /// The control: documents that the default pipeline DOES load the per-environment file.
    /// If the framework ever stops doing so, this goes red and the guard below is dead weight.
    /// </summary>
    [Fact]
    public void The_default_pipeline_loads_a_per_environment_file_beside_the_binary()
    {
        var builder = ProductionBuilder();

        Assert.Equal("leaked", builder.Configuration["Whetstone:PlantedByTest"]);
    }

    [Fact]
    public void The_composition_root_strips_the_per_environment_file_from_the_layering()
    {
        var builder = ProductionBuilder();

        builder.AddWhetstoneOptions();

        Assert.Null(builder.Configuration["Whetstone:PlantedByTest"]);
    }
}
