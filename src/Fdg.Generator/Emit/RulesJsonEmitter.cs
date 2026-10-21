using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fdg.Generator.Rules;

namespace Fdg.Generator.Emit;

/// <summary>Writes <c>rules.json</c>, the manifest the CLI reads from the package to explain diagnostics.</summary>
public static class RulesJsonEmitter
{
    public const int ProtocolVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Emit(GeneratorInput input)
    {
        var json = new JsonObject
        {
            ["protocol"] = ProtocolVersion,
            ["version"] = input.Version,
            ["rules"] = new JsonArray(input.Rules.Select(RuleEntry).ToArray<JsonNode?>()),
            ["registeredDiagnostics"] = Strings(DiagnosticMap.Registered(input.Rules)),
            ["breakingFixDiagnostics"] = Strings(DiagnosticMap.BreakingFix(input.Rules)),
        };

        return json.ToJsonString(Options) + "\n";
    }

    private static JsonObject RuleEntry(Rule rule)
    {
        RuleMapping mapping = rule.Mapping;
        return new JsonObject
        {
            ["id"] = rule.Id,
            ["strength"] = rule.Strength.ToText(),
            ["text"] = rule.Text,
            ["section"] = rule.Section,
            ["diagnostics"] = Strings(mapping.Check.Diagnostics),
            ["autofix"] = RuleYaml.EnumText(mapping.Autofix),
            ["severity"] = new JsonObject
            {
                ["library"] = RuleText.Level(mapping.Severity.Library),
                ["application"] = RuleText.Level(mapping.Severity.Application),
                ["test"] = RuleText.Level(mapping.Severity.Test),
            },
        };
    }

    private static JsonArray Strings(IEnumerable<string> values)
    {
        return new JsonArray(values.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());
    }
}
