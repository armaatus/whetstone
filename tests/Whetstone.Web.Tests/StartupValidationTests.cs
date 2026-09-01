using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Whetstone.Web.Tests;

/// <summary>
/// Ticket 0.7, Web edition — same style as the Worker's StartupValidationTests: boot a host
/// through the composition root's own <see cref="OptionsWiring"/> over an empty builder, so
/// nothing on the machine can turn a red test green. Web binds only the database section;
/// that decision has its reasons on <see cref="OptionsWiring"/> itself.
/// </summary>
public class StartupValidationTests
{
    private static IHost BuildHost(Dictionary<string, string?> settings)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(settings);
        builder.AddWhetstoneOptions();
        return builder.Build();
    }

    [Fact]
    public void The_host_starts_when_the_connection_string_is_present()
    {
        using var host = BuildHost(new Dictionary<string, string?>
        {
            ["Whetstone:Database:ConnectionString"] = "Host=localhost;Database=whetstone;Username=whetstone_app;Password=test-db-password",
        });

        host.Start();
    }

    [Fact]
    public void A_missing_connection_string_fails_startup()
    {
        using var host = BuildHost([]);

        var exception = Assert.Throws<OptionsValidationException>(host.Start);

        Assert.Contains("Whetstone:Database:ConnectionString", exception.Message, StringComparison.Ordinal);
    }
}
