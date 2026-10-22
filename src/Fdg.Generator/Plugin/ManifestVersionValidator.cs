using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using Fdg.Generator.Rules;

namespace Fdg.Generator.Plugin;

/// <summary>
/// Checks that both plugin manifests and the marketplace carry the <c>&lt;Version&gt;</c> of
/// <c>Directory.Build.props</c>, the one version of the package, the tool and the plugin.
/// </summary>
public static class ManifestVersionValidator
{
    public const string PropsFile = "Directory.Build.props";
    public const string ClaudeManifest = "plugins/framework-design-guidelines/.claude-plugin/plugin.json";
    public const string CodexManifest = "plugins/framework-design-guidelines/.codex-plugin/plugin.json";
    public const string Marketplace = ".claude-plugin/marketplace.json";

    public static IReadOnlyList<CheckError> Validate(string repositoryRoot)
    {
        var errors = new List<CheckError>();
        string? expected = ReadPropsVersion(repositoryRoot, errors);
        if (expected is null)
        {
            return errors;
        }

        foreach (string manifest in new[] { ClaudeManifest, CodexManifest })
        {
            JsonNode? json = ReadJson(repositoryRoot, manifest, errors);
            if (json is not null)
            {
                Compare(manifest, "version", json["version"], expected, required: true, errors);
            }
        }

        JsonNode? marketplace = ReadJson(repositoryRoot, Marketplace, errors);
        if (marketplace is not null)
        {
            Compare(Marketplace, "version", marketplace["version"], expected, required: true, errors);
            if (marketplace["plugins"] is JsonArray plugins)
            {
                for (int i = 0; i < plugins.Count; i++)
                {
                    // An entry may leave its version to plugin.json; one it does declare must agree.
                    Compare(Marketplace, $"plugins[{i}].version", plugins[i]?["version"], expected, required: false, errors);
                }
            }
        }

        return errors;
    }

    private static string? ReadPropsVersion(string repositoryRoot, List<CheckError> errors)
    {
        string path = Path.Combine(repositoryRoot, PropsFile);
        if (!File.Exists(path))
        {
            errors.Add(Error(PropsFile, $"{path} does not exist."));
            return null;
        }

        try
        {
            string? version = XDocument.Load(path).Descendants("Version").Select(e => e.Value.Trim()).FirstOrDefault();
            if (string.IsNullOrEmpty(version))
            {
                errors.Add(Error(PropsFile, "<Version> is not set."));
                return null;
            }

            return version;
        }
        catch (XmlException exception)
        {
            errors.Add(Error(PropsFile, exception.Message));
            return null;
        }
    }

    private static JsonNode? ReadJson(string repositoryRoot, string relativePath, List<CheckError> errors)
    {
        string path = Path.Combine(repositoryRoot, relativePath);
        if (!File.Exists(path))
        {
            errors.Add(Error(relativePath, $"{path} does not exist."));
            return null;
        }

        try
        {
            return JsonNode.Parse(File.ReadAllText(path));
        }
        catch (JsonException exception)
        {
            errors.Add(Error(relativePath, exception.Message));
            return null;
        }
    }

    private static void Compare(
        string file,
        string field,
        JsonNode? value,
        string expected,
        bool required,
        List<CheckError> errors)
    {
        string? actual = value is JsonValue jsonValue && jsonValue.TryGetValue(out string? text) ? text : null;
        if (actual is null)
        {
            if (required || value is not null)
            {
                errors.Add(Error(file, $"'{field}' must be the string \"{expected}\" from {PropsFile}."));
            }
        }
        else if (actual != expected)
        {
            errors.Add(Error(file, $"'{field}' is \"{actual}\" but <Version> in {PropsFile} is \"{expected}\"."));
        }
    }

    private static CheckError Error(string file, string message)
    {
        return new CheckError(file, CheckError.NoRule, message);
    }
}
