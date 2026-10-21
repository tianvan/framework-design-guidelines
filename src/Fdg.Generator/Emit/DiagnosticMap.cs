using Fdg.Generator.Rules;

namespace Fdg.Generator.Emit;

/// <summary>What the rules say about each diagnostic id: its severity per profile and whether fixing it can break callers.</summary>
internal static class DiagnosticMap
{
    /// <summary>
    /// The severity of each diagnostic in <paramref name="profile"/>: the highest level among the rules that map it,
    /// where <c>none</c> is lowest. Sorted by id.
    /// </summary>
    public static IReadOnlyDictionary<string, SeverityLevel> Severities(IEnumerable<Rule> rules, string profile)
    {
        var severities = new SortedDictionary<string, SeverityLevel>(StringComparer.Ordinal);
        foreach (Rule rule in rules)
        {
            SeverityLevel level = RuleText.Severity(rule, profile);
            foreach (string id in rule.Mapping.Check.Diagnostics)
            {
                severities[id] = severities.TryGetValue(id, out SeverityLevel existing) && existing > level ? existing : level;
            }
        }

        return severities;
    }

    /// <summary>Every diagnostic id any rule maps, sorted.</summary>
    public static IReadOnlyList<string> Registered(IEnumerable<Rule> rules)
    {
        return rules
            .SelectMany(r => r.Mapping.Check.Diagnostics)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Diagnostics whose code fix may break callers. One breaking rule is enough: a diagnostic's fix cannot tell
    /// which of the rules mapping it it is serving, so it must not be applied automatically.
    /// </summary>
    public static IReadOnlyList<string> BreakingFix(IEnumerable<Rule> rules)
    {
        return rules
            .Where(r => r.Mapping.Autofix == AutofixKind.Breaking)
            .SelectMany(r => r.Mapping.Check.Diagnostics)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
    }
}
