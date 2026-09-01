using System.Text.Json;

namespace Whetstone.Architecture.Tests;

/// <summary>
/// Spec 13.5, as executable rules over the committed appsettings.json files: they hold
/// non-secret defaults and structure only. Like <see cref="ProjectReferenceTests"/>, this
/// reads the repo rather than IL — a JSON file has no IL to inspect.
/// </summary>
public class AppSettingsStructureTests
{
    /// <summary>
    /// Every secret-bearing key each host's appsettings.json must carry, empty. Present, so
    /// the shape is documented and an absence is visible; empty, so the value can only come
    /// from user secrets in development or environment variables in production. A key added
    /// to an options class but not listed here still fails the sweep test below if it ever
    /// gains a committed value.
    /// </summary>
    private static readonly Dictionary<string, string[]> SecretKeys = new(StringComparer.Ordinal)
    {
        ["Whetstone.Worker"] =
        [
            "Whetstone:Ai:ApiKey",
            "Whetstone:Corpus:RepoAccessToken",
            "Whetstone:Lens:RegistryApiKey",
            "Whetstone:Database:ConnectionString",
        ],
        ["Whetstone.Web"] =
        [
            "Whetstone:Database:ConnectionString",
        ],
    };

    /// <summary>
    /// Property names that carry secrets when they carry anything. The sweep is deliberately
    /// broader than <see cref="SecretKeys"/>: it also catches a secret-shaped key someone adds
    /// tomorrow, anywhere in the file.
    /// </summary>
    private static readonly string[] SecretNameFragments =
        ["apikey", "token", "password", "secret", "connectionstring", "credential"];

    public static TheoryData<string> Hosts() => [.. SecretKeys.Keys];

    [Theory]
    [MemberData(nameof(Hosts))]
    public void Every_secret_bearing_key_is_present_and_empty(string host)
    {
        using var settings = Load(host);

        var violations = SecretKeys[host]
            .Select(key => (key, value: ValueAt(settings.RootElement, key)))
            .Where(x => x.value is not "")
            .Select(x => x.value is null
                ? $"{host}/appsettings.json is missing {x.key} — the structure documents the shape, so the key must be present (and empty)"
                : $"{host}/appsettings.json carries a value for {x.key} — secrets never live in the repo tree (spec 13.5)")
            .ToArray();

        Assert.True(violations.Length == 0, string.Join(Environment.NewLine, violations));
    }

    [Theory]
    [MemberData(nameof(Hosts))]
    public void No_secret_shaped_key_anywhere_carries_a_value(string host)
    {
        using var settings = Load(host);
        var violations = new List<string>();

        Sweep(settings.RootElement, "", violations, host);

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// Spec 13.5: dev secrets go in user secrets, never a per-environment JSON file — #1 put
    /// appsettings.Development.json in .gitignore, and this fails the build on the machine
    /// where one is created anyway, before the mistake can even look like it worked.
    /// </summary>
    [Fact]
    public void No_per_environment_appsettings_file_exists()
    {
        var offenders = SecretKeys.Keys
            .SelectMany(host => Directory.EnumerateFiles(
                Path.Combine(SolutionArchitecture.RepoRoot(), "src", host), "appsettings.*.json"))
            .Select(Path.GetFileName)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "per-environment appsettings files found (move the values to dotnet user-secrets): "
            + string.Join(", ", offenders));
    }

    private static JsonDocument Load(string host)
    {
        var path = Path.Combine(SolutionArchitecture.RepoRoot(), "src", host, "appsettings.json");

        // The configuration stack allows comments in appsettings.json, so the test must too.
        return JsonDocument.Parse(
            File.ReadAllText(path),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
    }

    /// <summary>
    /// Resolves a colon-separated configuration key against the JSON tree. Null means the key
    /// is absent; otherwise the string value (non-string leaves come back as raw text, which
    /// no secret-bearing key should be anyway).
    /// </summary>
    private static string? ValueAt(JsonElement root, string key)
    {
        JsonElement current = root;

        foreach (string segment in key.Split(':'))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current.ValueKind == JsonValueKind.String ? current.GetString() : current.GetRawText();
    }

    private static void Sweep(JsonElement element, string path, List<string> violations, string host)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    var childPath = path.Length == 0 ? property.Name : path + ":" + property.Name;

                    // A value is anything but the empty string: numbers and booleans carry one
                    // too. Objects and arrays are not flagged here because the recursion below
                    // judges each of their leaves on its own name and value; null carries
                    // nothing (and the named-key test above already demands "" over null).
                    bool carriesValue = property.Value.ValueKind switch
                    {
                        JsonValueKind.String => property.Value.GetString() is not "",
                        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => true,
                        _ => false,
                    };

                    if (carriesValue
                        && SecretNameFragments.Any(fragment =>
                            property.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
                    {
                        violations.Add(
                            $"{host}/appsettings.json: {childPath} looks secret-bearing and carries a value — secrets never live in the repo tree (spec 13.5)");
                    }

                    Sweep(property.Value, childPath, violations, host);
                }

                break;

            case JsonValueKind.Array:
                int index = 0;
                foreach (JsonElement item in element.EnumerateArray())
                {
                    Sweep(item, path + ":" + index, violations, host);
                    index++;
                }

                break;

            default:
                break;
        }
    }
}
