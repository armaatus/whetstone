using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Whetstone.Worker.Tests;

/// <summary>
/// Ticket 0.7: a missing secret is a boot failure, not a 3am NullReferenceException. Each test
/// boots a host through the Worker's own <see cref="OptionsWiring"/> — the code Program.cs
/// runs — over an empty builder, so nothing from the machine (user secrets, environment,
/// appsettings on disk) can leak in and turn a red test green.
/// </summary>
public class StartupValidationTests
{
    /// <summary>
    /// Every key the Worker's wiring requires, with recognisable dummy values. Tests remove or
    /// blank exactly one, so a failure is attributable to that key alone.
    /// </summary>
    private static Dictionary<string, string?> CompleteSettings() => new()
    {
        ["Whetstone:Ai:ApiKey"] = "test-ai-key",
        ["Whetstone:Corpus:RepoAccessToken"] = "test-corpus-token",
        ["Whetstone:Database:ConnectionString"] = "Host=localhost;Database=whetstone;Username=whetstone_app;Password=test-db-password",
    };

    private static IHost BuildHost(Dictionary<string, string?> settings)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(settings);
        builder.AddWhetstoneOptions();
        return builder.Build();
    }

    /// <summary>
    /// The control: with every secret present the host starts. Without this, every red test
    /// below could be red for a reason other than the missing key.
    /// </summary>
    [Fact]
    public void The_host_starts_when_every_secret_is_present()
    {
        using var host = BuildHost(CompleteSettings());

        host.Start();
    }

    [Fact]
    public void A_missing_AI_api_key_fails_startup()
    {
        var settings = CompleteSettings();
        settings.Remove("Whetstone:Ai:ApiKey");
        using var host = BuildHost(settings);

        var exception = Assert.Throws<OptionsValidationException>(host.Start);

        Assert.Contains("Whetstone:Ai:ApiKey", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The empty string is the shape appsettings.json commits (structure with blank secrets),
    /// so "present but empty" must fail exactly like "absent".
    /// </summary>
    [Fact]
    public void An_empty_AI_api_key_fails_startup_like_a_missing_one()
    {
        var settings = CompleteSettings();
        settings["Whetstone:Ai:ApiKey"] = "";
        using var host = BuildHost(settings);

        var exception = Assert.Throws<OptionsValidationException>(host.Start);

        Assert.Contains("Whetstone:Ai:ApiKey", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The corpus token gets exactly the AI key's treatment: a system that boots without corpus
    /// access starts healthy and produces zero candidates forever (ticket 0.7, ADR-0006).
    /// </summary>
    [Fact]
    public void A_missing_corpus_credential_fails_startup()
    {
        var settings = CompleteSettings();
        settings.Remove("Whetstone:Corpus:RepoAccessToken");
        using var host = BuildHost(settings);

        var exception = Assert.Throws<OptionsValidationException>(host.Start);

        Assert.Contains("Whetstone:Corpus:RepoAccessToken", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_connection_string_fails_startup()
    {
        var settings = CompleteSettings();
        settings.Remove("Whetstone:Database:ConnectionString");
        using var host = BuildHost(settings);

        var exception = Assert.Throws<OptionsValidationException>(host.Start);

        Assert.Contains("Whetstone:Database:ConnectionString", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Spec 13.5 / 7.8: a validation message names the key and never echoes a value. The
    /// missing key has no value to echo, so the real risk is the message dragging in the
    /// secrets that WERE provided — assert none of them appear.
    /// </summary>
    [Fact]
    public void Validation_messages_never_echo_a_configured_value()
    {
        var settings = CompleteSettings();
        settings.Remove("Whetstone:Corpus:RepoAccessToken");
        using var host = BuildHost(settings);

        var exception = Assert.Throws<OptionsValidationException>(host.Start);

        Assert.DoesNotContain("test-ai-key", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("test-db-password", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// No registry and no pins is a valid state until Epic 2.5 — requiring a registry before
    /// the Lens pipeline exists would just teach people to fill it with a placeholder.
    /// </summary>
    [Fact]
    public void The_host_starts_with_no_lens_registry_and_no_pins()
    {
        var settings = CompleteSettings();
        settings["Whetstone:Lens:Registry"] = "";
        using var host = BuildHost(settings);

        host.Start();
    }

    /// <summary>
    /// ADR-0007 §1: a Lens is pinned as id@version plus a content hash. A pin missing its hash
    /// is exactly the "resolve to whatever upstream has now" hole pinning exists to close, so
    /// it fails at boot, naming the full key including the index.
    /// </summary>
    [Fact]
    public void A_lens_pin_without_a_content_hash_fails_startup()
    {
        var settings = CompleteSettings();
        settings["Whetstone:Lens:Registry"] = "https://registry.example.test";
        settings["Whetstone:Lens:Pins:0:Id"] = "csharp-idioms";
        settings["Whetstone:Lens:Pins:0:Version"] = "1.2.0";
        using var host = BuildHost(settings);

        var exception = Assert.Throws<OptionsValidationException>(host.Start);

        Assert.Contains("Whetstone:Lens:Pins:0:ContentHash", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The Lens messages are the only ones built by string interpolation (the pin index goes
    /// into the key path), so they are the likeliest place for a value to slip into a message.
    /// Configure real-looking values around a failing pin and assert none of them appear.
    /// </summary>
    [Fact]
    public void Lens_validation_messages_never_echo_configured_values()
    {
        var settings = CompleteSettings();
        settings["Whetstone:Lens:Registry"] = "https://registry.example.test";
        settings["Whetstone:Lens:RegistryApiKey"] = "test-registry-key";
        settings["Whetstone:Lens:Pins:0:Id"] = "csharp-idioms";
        settings["Whetstone:Lens:Pins:0:Version"] = "9.9.9";
        using var host = BuildHost(settings);

        var exception = Assert.Throws<OptionsValidationException>(host.Start);

        Assert.DoesNotContain("test-registry-key", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("registry.example.test", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("csharp-idioms", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("9.9.9", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_lens_pin_without_a_registry_fails_startup()
    {
        var settings = CompleteSettings();
        settings["Whetstone:Lens:Pins:0:Id"] = "csharp-idioms";
        settings["Whetstone:Lens:Pins:0:Version"] = "1.2.0";
        settings["Whetstone:Lens:Pins:0:ContentHash"] = "sha256:0000000000000000000000000000000000000000000000000000000000000000";
        using var host = BuildHost(settings);

        var exception = Assert.Throws<OptionsValidationException>(host.Start);

        Assert.Contains("Whetstone:Lens:Registry", exception.Message, StringComparison.Ordinal);
    }
}
