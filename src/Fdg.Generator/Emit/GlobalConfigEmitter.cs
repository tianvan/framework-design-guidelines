using System.Text;
using Fdg.Generator.Rules;

namespace Fdg.Generator.Emit;

/// <summary>Writes the globalconfig of one profile: base options, the rules' option lines, and diagnostic severities.</summary>
public static class GlobalConfigEmitter
{
    private const string NamingRulePrefix = "dotnet_naming_rule.";

    private static readonly string[] CodeStylePrefixes = { "dotnet_style_", "csharp_style_", "csharp_prefer_" };

    private static readonly string[] CodeStyleKeys = { "csharp_using_directive_placement", "csharp_preferred_modifier_order" };

    public static string Emit(GeneratorInput input, string profile)
    {
        if (!input.Profiles.TryGetValue(profile, out IReadOnlyList<string>? baseOptions))
        {
            throw new GenerationException($"profiles.yaml has no profile '{profile}'.");
        }

        var optionLines = input.Rules.ToDictionary(r => r.Id, r => OptionLines(r, RuleText.Severity(r, profile)));
        List<ConfigLine> baseLines = baseOptions.Select(o => ParseLine(o, ProfileFile.FileName)).ToList();

        // A conflict is an error in the data whichever profile is built, so every rule is checked, not only those on.
        CheckConflicts(input, baseLines, optionLines);

        var text = new StringBuilder();
        text.Append("is_global = true\n");
        // Above the SDK's AnalysisLevel globalconfigs (-100, per category -99), below any user globalconfig (0 or 100).
        text.Append("global_level = -1\n");
        text.Append(Header.GlobalConfig).Append('\n');

        IReadOnlyDictionary<string, SeverityLevel> severities = DiagnosticMap.Severities(input.Rules, profile);
        var written = new HashSet<string>(StringComparer.Ordinal);
        text.Append("\n# Profile options\n");
        foreach (ConfigLine line in baseLines)
        {
            AppendNew(text, written, line);
        }

        foreach (Rule rule in input.Rules.Where(r => RuleText.Severity(r, profile) != SeverityLevel.None))
        {
            var block = new StringBuilder();
            foreach (ConfigLine line in optionLines[rule.Id])
            {
                AppendNew(block, written, WithSeverity(line, rule, severities));
            }

            if (block.Length > 0)
            {
                text.Append("\n# ").Append(rule.Id).Append('\n').Append(block);
            }
        }

        // Written for every diagnostic, none included, so that an SDK default never applies to a rule that is off.
        text.Append("\n# Diagnostic severities\n");
        foreach ((string id, SeverityLevel level) in severities)
        {
            text.Append("dotnet_diagnostic.").Append(id).Append(".severity = ").Append(RuleText.Level(level)).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>
    /// The rule's option lines plus one <c>.severity</c> line per naming rule it defines, taken from the rule's severity
    /// in the profile. A severity line written in <c>config</c> is only a placeholder and is replaced.
    /// </summary>
    private static List<ConfigLine> OptionLines(Rule rule, SeverityLevel severity)
    {
        var lines = new List<ConfigLine>();
        var namingRules = new List<string>();
        foreach (string option in rule.Mapping.Config)
        {
            ConfigLine line = ParseLine(option, rule.Id);
            if (line.Key.StartsWith(NamingRulePrefix, StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = line.Key[NamingRulePrefix.Length..].Split('.');
                if (parts.Length == 2 && !namingRules.Contains(parts[0]))
                {
                    namingRules.Add(parts[0]);
                }

                if (parts.Length == 2 && parts[1].Equals("severity", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            lines.Add(line);
        }

        lines.AddRange(namingRules.Select(name => new ConfigLine($"{NamingRulePrefix}{name}.severity", RuleText.Level(severity))));
        return lines;
    }

    /// <summary>
    /// Appends <c>:severity</c> to a code style option, taken from the IDE diagnostics its rule maps. <c>dotnet format</c>
    /// decides which IDE rules to fix from that suffix and ignores <c>dotnet_diagnostic</c> entries of a globalconfig;
    /// the build honors either. Formatting options (IDE0055) take no suffix, and a suffix written in the data is kept.
    /// </summary>
    private static ConfigLine WithSeverity(ConfigLine line, Rule rule, IReadOnlyDictionary<string, SeverityLevel> severities)
    {
        bool isCodeStyle = CodeStylePrefixes.Any(p => line.Key.StartsWith(p, StringComparison.OrdinalIgnoreCase))
            || CodeStyleKeys.Contains(line.Key, StringComparer.OrdinalIgnoreCase);
        if (!isCodeStyle || line.Value.Contains(':', StringComparison.Ordinal))
        {
            return line;
        }

        SeverityLevel[] levels = rule.Mapping.Check.Diagnostics
            .Where(id => id.StartsWith("IDE", StringComparison.Ordinal) && severities.ContainsKey(id))
            .Select(id => severities[id])
            .ToArray();
        return levels.Length == 0 ? line : line with { Value = $"{line.Value}:{RuleText.Level(levels.Max())}" };
    }

    private static ConfigLine ParseLine(string text, string owner)
    {
        return ConfigLine.TryParse(text, out ConfigLine? line)
            ? line
            : throw new GenerationException($"{owner}: '{text}' is not a 'key = value' option line.");
    }

    private static void AppendNew(StringBuilder text, HashSet<string> written, ConfigLine line)
    {
        if (written.Add(line.Key.ToLowerInvariant()))
        {
            text.Append(line).Append('\n');
        }
    }

    private static void CheckConflicts(
        GeneratorInput input,
        List<ConfigLine> baseLines,
        Dictionary<string, List<ConfigLine>> optionLines)
    {
        var seen = new Dictionary<string, (string Value, string Owner)>(StringComparer.Ordinal);

        void Check(ConfigLine line, string owner)
        {
            string key = line.Key.ToLowerInvariant();
            if (!seen.TryGetValue(key, out (string Value, string Owner) earlier))
            {
                seen.Add(key, (line.Value, owner));
            }
            else if (earlier.Value != line.Value)
            {
                throw new GenerationException(
                    $"Conflicting values for '{line.Key}': '{earlier.Value}' set by {earlier.Owner}, '{line.Value}' set by {owner}.");
            }
        }

        foreach (ConfigLine line in baseLines)
        {
            Check(line, ProfileFile.FileName);
        }

        foreach (Rule rule in input.Rules)
        {
            foreach (ConfigLine line in optionLines[rule.Id])
            {
                Check(line, rule.Id);
            }
        }
    }
}
