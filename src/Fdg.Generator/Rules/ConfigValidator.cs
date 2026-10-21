using System.Text.RegularExpressions;

namespace Fdg.Generator.Rules;

/// <summary>
/// Checks the <c>config</c> option lines of all rules and the base options in <c>profiles.yaml</c>: syntax,
/// .editorconfig naming-rule structure, and that no two lines set one key to different values.
/// </summary>
public static class ConfigValidator
{
    private const string NamingRulePrefix = "dotnet_naming_rule.";
    private const string NamingSymbolsPrefix = "dotnet_naming_symbols.";
    private const string NamingStylePrefix = "dotnet_naming_style.";
    private const string NamingDiagnostic = "IDE1006";

    private static readonly Regex SeveritySuffix = new(
        @":\s*(none|silent|refactoring|suggestion|warning|error|default)$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex NamePattern = new(@"^[a-z0-9_]+$", RegexOptions.CultureInvariant);

    private static readonly Dictionary<string, HashSet<string>?> SymbolsProperties = new(StringComparer.Ordinal)
    {
        ["applicable_kinds"] = new(StringComparer.Ordinal)
        {
            "*", "namespace", "class", "struct", "interface", "enum", "property", "method", "field", "event",
            "delegate", "parameter", "type_parameter", "local", "local_function",
        },
        ["applicable_accessibilities"] = new(StringComparer.Ordinal)
        {
            "*", "public", "internal", "friend", "private", "protected", "protected_internal", "protected_friend",
            "private_protected", "local",
        },
        ["required_modifiers"] = new(StringComparer.Ordinal)
        {
            "*", "abstract", "must_inherit", "async", "const", "readonly", "static", "shared",
        },
    };

    private static readonly Dictionary<string, HashSet<string>?> StyleProperties = new(StringComparer.Ordinal)
    {
        ["capitalization"] = new(StringComparer.Ordinal)
        {
            "pascal_case", "camel_case", "first_word_upper", "all_upper", "all_lower",
        },
        ["required_prefix"] = null,
        ["required_suffix"] = null,
        ["word_separator"] = null,
    };

    public static IReadOnlyList<CheckError> ValidateProfiles(IReadOnlyDictionary<string, IReadOnlyList<string>> profiles)
    {
        var errors = new List<CheckError>();
        foreach (string name in ProfileFile.Names.Where(n => !profiles.ContainsKey(n)))
        {
            errors.Add(ProfileError($"profile '{name}' is missing."));
        }

        foreach ((string name, IReadOnlyList<string> lines) in profiles)
        {
            if (!ProfileFile.Names.Contains(name))
            {
                errors.Add(ProfileError(
                    $"unknown profile '{name}'; expected {string.Join(", ", ProfileFile.Names.Select(n => $"'{n}'"))}."));
            }

            foreach (string line in lines.Where(l => !ConfigLine.TryParse(l, out _)))
            {
                errors.Add(ProfileError($"profile '{name}': '{line}' is not a 'key = value' option line."));
            }
        }

        return errors;
    }

    public static IReadOnlyList<CheckError> ValidateRules(
        IReadOnlyList<ChapterRules> chapters,
        IReadOnlyDictionary<string, IReadOnlyList<string>> profiles)
    {
        var state = new State(profiles);
        foreach (ChapterRules chapter in chapters)
        {
            foreach (Rule rule in chapter.Rules)
            {
                ValidateRule(state, chapter.Chapter.Rules, rule);
            }
        }

        state.ResolveNamingReferences();
        return state.Errors;
    }

    private static CheckError ProfileError(string message)
    {
        return new CheckError(ProfileFile.FileName, CheckError.NoRule, message);
    }

    private static void ValidateRule(State state, string file, Rule rule)
    {
        void Report(string message) => state.Errors.Add(new CheckError(file, rule.Id, message));

        var namingRules = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (string text in rule.Mapping.Config)
        {
            if (!ConfigLine.TryParse(text, out ConfigLine? line))
            {
                Report($"config '{text}' is not a 'key = value' option line.");
                continue;
            }

            string key = line.Key.ToLowerInvariant();
            if (key.StartsWith("dotnet_diagnostic.", StringComparison.Ordinal)
                || key.StartsWith("dotnet_analyzer_diagnostic.", StringComparison.Ordinal))
            {
                Report($"config '{line}' sets a severity; severities come from the severity field.");
                continue;
            }

            if (SeveritySuffix.IsMatch(line.Value))
            {
                Report($"config '{line}' has a ':severity' suffix; severities come from the severity field.");
                continue;
            }

            if (state.ProfileKeys.Contains(key))
            {
                Report($"config key '{line.Key}' is set per profile in {ProfileFile.FileName}.");
                continue;
            }

            if (state.Seen.TryGetValue(key, out (ConfigLine Line, string RuleId) earlier))
            {
                if (earlier.Line.Value != line.Value)
                {
                    Report($"config '{line}' conflicts with '{earlier.Line}' set by {earlier.RuleId}.");
                }
            }
            else
            {
                state.Seen.Add(key, (line, rule.Id));
            }

            if (key.StartsWith(NamingRulePrefix, StringComparison.Ordinal))
            {
                ValidateNamingRule(state, file, rule, line, namingRules, Report);
            }
            else if (key.StartsWith(NamingSymbolsPrefix, StringComparison.Ordinal))
            {
                ValidateNamingDefinition(line, NamingSymbolsPrefix, SymbolsProperties, state.Symbols, Report);
            }
            else if (key.StartsWith(NamingStylePrefix, StringComparison.Ordinal))
            {
                ValidateNamingDefinition(line, NamingStylePrefix, StyleProperties, state.Styles, Report);
            }
        }

        foreach ((string name, HashSet<string> properties) in namingRules)
        {
            foreach (string required in new[] { "symbols", "style" }.Where(p => !properties.Contains(p)))
            {
                Report($"naming rule '{name}' has no '{required}'.");
            }
        }

        if (namingRules.Count > 0 && !rule.Mapping.Check.Diagnostics.Contains(NamingDiagnostic))
        {
            Report($"naming rules are reported as {NamingDiagnostic}; add it to check.diagnostics.");
        }
    }

    private static void ValidateNamingRule(
        State state,
        string file,
        Rule rule,
        ConfigLine line,
        Dictionary<string, HashSet<string>> namingRules,
        Action<string> report)
    {
        if (!TrySplitNamingKey(line.Key, NamingRulePrefix, out string name, out string property))
        {
            report($"config key '{line.Key}' is not '{NamingRulePrefix}<name>.<property>'.");
            return;
        }

        string id = rule.Id.ToLowerInvariant();
        if (name != id && !name.StartsWith(id + "_", StringComparison.Ordinal))
        {
            report($"naming rule '{name}' must be named '{id}' or start with '{id}_'.");
        }

        switch (property)
        {
            case "symbols":
                state.References.Add((new CheckError(file, rule.Id, string.Empty), "symbols", line.Value));
                break;
            case "style":
                state.References.Add((new CheckError(file, rule.Id, string.Empty), "style", line.Value));
                break;
            case "severity":
                report($"config '{line}' sets a naming rule severity; it comes from the severity field.");
                return;
            default:
                report($"naming rule property '{property}' is unknown; use 'symbols' and 'style'.");
                return;
        }

        if (!namingRules.TryGetValue(name, out HashSet<string>? properties))
        {
            properties = new HashSet<string>(StringComparer.Ordinal);
            namingRules.Add(name, properties);
        }

        properties.Add(property);
    }

    private static void ValidateNamingDefinition(
        ConfigLine line,
        string prefix,
        Dictionary<string, HashSet<string>?> allowed,
        Dictionary<string, HashSet<string>> definitions,
        Action<string> report)
    {
        if (!TrySplitNamingKey(line.Key, prefix, out string name, out string property))
        {
            report($"config key '{line.Key}' is not '{prefix}<name>.<property>'.");
            return;
        }

        if (!allowed.TryGetValue(property, out HashSet<string>? values))
        {
            report($"'{property}' is not a {prefix.TrimEnd('.')} property; expected {string.Join(", ", allowed.Keys)}.");
            return;
        }

        if (values is not null)
        {
            string[] items = line.Value.Split(',', StringSplitOptions.TrimEntries);
            foreach (string item in items.Where(i => !values.Contains(i)))
            {
                report($"'{item}' is not a valid value of {prefix}{name}.{property}.");
            }

            if (property == "capitalization" && items.Length != 1)
            {
                report($"{prefix}{name}.capitalization takes a single value.");
            }
        }

        if (!definitions.TryGetValue(name, out HashSet<string>? properties))
        {
            properties = new HashSet<string>(StringComparer.Ordinal);
            definitions.Add(name, properties);
        }

        properties.Add(property);
    }

    private static bool TrySplitNamingKey(string key, string prefix, out string name, out string property)
    {
        string[] parts = key[prefix.Length..].Split('.');
        name = parts[0];
        property = parts.Length == 2 ? parts[1] : string.Empty;
        return parts.Length == 2 && NamePattern.IsMatch(name) && property.Length > 0;
    }

    private sealed class State
    {
        public State(IReadOnlyDictionary<string, IReadOnlyList<string>> profiles)
        {
            ProfileKeys = profiles.Values
                .SelectMany(lines => lines)
                .Select(l => ConfigLine.TryParse(l, out ConfigLine? line) ? line.Key.ToLowerInvariant() : null)
                .OfType<string>()
                .ToHashSet(StringComparer.Ordinal);
        }

        public List<CheckError> Errors { get; } = new();

        public HashSet<string> ProfileKeys { get; }

        public Dictionary<string, (ConfigLine Line, string RuleId)> Seen { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, HashSet<string>> Symbols { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, HashSet<string>> Styles { get; } = new(StringComparer.Ordinal);

        /// <summary>Symbols and style names used by naming rules, with the referencing rule as an error template.</summary>
        public List<(CheckError Origin, string Kind, string Name)> References { get; } = new();

        public void ResolveNamingReferences()
        {
            foreach ((CheckError origin, string kind, string name) in References)
            {
                (Dictionary<string, HashSet<string>> definitions, string prefix, string required) = kind == "symbols"
                    ? (Symbols, NamingSymbolsPrefix, "applicable_kinds")
                    : (Styles, NamingStylePrefix, "capitalization");

                if (!definitions.TryGetValue(name, out HashSet<string>? properties))
                {
                    Errors.Add(origin with { Message = $"naming {kind} '{name}' is not defined by any '{prefix}{name}.*' line." });
                }
                else if (!properties.Contains(required))
                {
                    Errors.Add(origin with { Message = $"naming {kind} '{name}' needs '{prefix}{name}.{required}'." });
                }
            }
        }
    }
}
