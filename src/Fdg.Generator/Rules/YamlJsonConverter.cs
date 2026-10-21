using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Fdg.Generator.Rules;

/// <summary>
/// Converts YAML to JSON for schema validation, resolving plain scalars by the YAML 1.2 core schema.
/// Unlike YAML 1.1, <c>yes</c>, <c>no</c>, <c>on</c> and <c>off</c> stay strings (for example <c>autofix: no</c>).
/// </summary>
public static class YamlJsonConverter
{
    private const string StringTag = "tag:yaml.org,2002:str";

    private static readonly Regex IntegerPattern = new(@"^[-+]?[0-9]+$", RegexOptions.CultureInvariant);

    private static readonly Regex FloatPattern = new(
        @"^[-+]?(\.[0-9]+|[0-9]+(\.[0-9]*)?)([eE][-+]?[0-9]+)?$",
        RegexOptions.CultureInvariant);

    /// <summary>Converts the first document of <paramref name="yaml"/>; returns null for an empty stream.</summary>
    /// <exception cref="YamlException">The text is not valid YAML or repeats a mapping key.</exception>
    public static JsonNode? ToJson(string yaml)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));
        return stream.Documents.Count == 0 ? null : Convert(stream.Documents[0].RootNode);
    }

    private static JsonNode? Convert(YamlNode node)
    {
        switch (node)
        {
            case YamlScalarNode scalar:
                return ConvertScalar(scalar);
            case YamlSequenceNode sequence:
                return new JsonArray(sequence.Children.Select(Convert).ToArray());
            case YamlMappingNode mapping:
                var json = new JsonObject();
                foreach ((YamlNode key, YamlNode value) in mapping.Children)
                {
                    string name = key is YamlScalarNode { Value: { } text }
                        ? text
                        : throw new YamlException(key.Start, key.End, "Mapping keys must be scalars.");
                    if (json.ContainsKey(name))
                    {
                        throw new YamlException(key.Start, key.End, $"Duplicate key '{name}'.");
                    }

                    json[name] = Convert(value);
                }

                return json;
            default:
                throw new YamlException(node.Start, node.End, $"Unsupported YAML node {node.NodeType}.");
        }
    }

    private static JsonNode? ConvertScalar(YamlScalarNode scalar)
    {
        string text = scalar.Value ?? string.Empty;
        if (scalar.Style != ScalarStyle.Plain || (!scalar.Tag.IsEmpty && scalar.Tag.Value == StringTag))
        {
            return JsonValue.Create(text);
        }

        switch (text)
        {
            case "" or "~" or "null" or "Null" or "NULL":
                return null;
            case "true" or "True" or "TRUE":
                return JsonValue.Create(true);
            case "false" or "False" or "FALSE":
                return JsonValue.Create(false);
        }

        if (IntegerPattern.IsMatch(text) && long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long integer))
        {
            return JsonValue.Create(integer);
        }

        if (FloatPattern.IsMatch(text))
        {
            return JsonValue.Create(double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture));
        }

        return JsonValue.Create(text);
    }
}
