using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using YamlDotNet.Core;

namespace Fdg.Generator.Rules;

/// <summary>Validates rule files against <c>rules/rules.schema.json</c>.</summary>
public sealed class SchemaValidator
{
    public const string FileName = "rules.schema.json";

    private static readonly EvaluationOptions Options = new() { OutputFormat = OutputFormat.Hierarchical };

    private readonly JsonSchema _schema;

    private SchemaValidator(JsonSchema schema)
    {
        _schema = schema;
    }

    public static SchemaValidator Load(string schemaPath)
    {
        // A private registry lets the same $id be loaded more than once per process.
        var options = new BuildOptions { SchemaRegistry = new SchemaRegistry() };
        return new SchemaValidator(JsonSchema.FromText(File.ReadAllText(schemaPath), options));
    }

    /// <summary>Validates the YAML text of the rule file <paramref name="fileName"/>.</summary>
    public IReadOnlyList<CheckError> Validate(string fileName, string yaml)
    {
        JsonNode? json;
        try
        {
            json = YamlJsonConverter.ToJson(yaml);
        }
        catch (YamlException exception)
        {
            return new[] { new CheckError(fileName, CheckError.NoRule, $"invalid YAML: {exception.Message}") };
        }

        EvaluationResults results = _schema.Evaluate(JsonSerializer.SerializeToElement(json), Options);
        if (results.IsValid)
        {
            return Array.Empty<CheckError>();
        }

        var errors = new List<CheckError>();
        CollectLeafErrors(results, json, fileName, errors);
        return errors.Distinct().ToList();
    }

    /// <summary>Reports the errors of the innermost failing schema nodes, skipping branches that passed.</summary>
    private static void CollectLeafErrors(EvaluationResults node, JsonNode? json, string fileName, List<CheckError> errors)
    {
        List<EvaluationResults> failedChildren = (node.Details ?? new List<EvaluationResults>())
            .Where(d => !d.IsValid)
            .ToList();
        if (failedChildren.Count > 0)
        {
            foreach (EvaluationResults child in failedChildren)
            {
                CollectLeafErrors(child, json, fileName, errors);
            }

            return;
        }

        string location = node.InstanceLocation.ToString();
        foreach (string message in node.Errors?.Values ?? Enumerable.Empty<string>())
        {
            errors.Add(new CheckError(fileName, RuleIdAt(json, location), $"{location}: {message}"));
        }
    }

    /// <summary>The id of the rule that contains the JSON pointer <paramref name="location"/>.</summary>
    private static string RuleIdAt(JsonNode? json, string location)
    {
        string[] segments = location.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (json is not JsonArray rules || segments.Length == 0 || !int.TryParse(segments[0], out int index)
            || index < 0 || index >= rules.Count)
        {
            return CheckError.NoRule;
        }

        return rules[index] is JsonObject rule && rule["id"] is JsonValue id && id.TryGetValue(out string? text)
            ? text
            : $"#{index}";
    }
}
