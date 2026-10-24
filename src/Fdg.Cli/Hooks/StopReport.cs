using System.Text;
using Fdg.Cli.Rules;

namespace Fdg.Cli.Hooks;

/// <summary>A diagnostic to report, with the rules it checks for the project's profile.</summary>
/// <param name="DisplayPath">Relative to the repository root, with forward slashes.</param>
/// <param name="SourceLine">The flagged line, trimmed; <see langword="null"/> when it cannot be read.</param>
public sealed record ReportedIssue(
    string DisplayPath,
    int Line,
    string RuleId,
    string Message,
    string? SourceLine,
    IReadOnlyList<RuleEntry> Rules);

/// <summary>The Stop hook's report: at most 20 issues sorted by file and line, each with at most two rules.</summary>
public static class StopReport
{
    public const int MaxIssues = 20;

    public const int MaxRulesPerIssue = 2;

    private static readonly HashSet<string> IgnoredWords = new(StringComparer.Ordinal)
    {
        "naming", "rule", "violation", "the", "and", "for", "this", "that", "with", "should", "must", "not",
        // IDE1006 says how a name fails, not which kind of symbol it names.
        "these", "words", "begin", "upper", "lower", "case", "characters", "cannot", "contain", "leading", "letter",
    };

    /// <param name="header">The first line.</param>
    public static string Format(IReadOnlyList<ReportedIssue> issues, string header)
    {
        var text = new StringBuilder();
        text.Append(header).Append('\n');
        foreach (ReportedIssue issue in Sort(issues).Take(MaxIssues))
        {
            text.Append(issue.DisplayPath).Append(':').Append(issue.Line).Append(' ')
                .Append(issue.RuleId).Append(' ').Append(issue.Message).Append('\n');
            if (!string.IsNullOrEmpty(issue.SourceLine))
            {
                text.Append("    ").Append(issue.SourceLine).Append('\n');
            }

            foreach (RuleEntry rule in issue.Rules.Take(MaxRulesPerIssue))
            {
                text.Append("    ").Append(rule.Id).Append(' ').Append(rule.Strength).Append(' ').Append(rule.Text).Append('\n');
            }
        }

        if (issues.Count > MaxIssues)
        {
            text.Append("... and ").Append(issues.Count - MaxIssues).Append(" more.\n");
        }

        return text.ToString();
    }

    public static IEnumerable<ReportedIssue> Sort(IEnumerable<ReportedIssue> issues) => issues
        .OrderBy(i => i.DisplayPath, StringComparer.Ordinal)
        .ThenBy(i => i.Line)
        .ThenBy(i => i.RuleId, StringComparer.Ordinal)
        .ThenBy(i => i.Message, StringComparer.Ordinal);

    /// <summary>
    /// The rules a diagnostic checks under <paramref name="profile"/>, most relevant first, at most
    /// <see cref="MaxRulesPerIssue"/>. For IDE1006, if any of its rules carries naming tags, the tags decide: only the
    /// rules that list the symbol kind and the failure of <paramref name="naming"/> are quoted, and none when it is
    /// <see langword="null"/> because the position or the message could not be read; the narrower a rule's tags (the
    /// fewer symbol kinds and failures it lists), the earlier it comes, so the rule written for this kind of symbol
    /// beats the general one. Otherwise the rules sharing more
    /// words with the message and the flagged line come first and the manifest's order is kept among equals. When
    /// there are more than <see cref="MaxRulesPerIssue"/>, only rules whose text has a word of the message that is
    /// not in the flagged line are kept, so a message that names no rule's subject quotes no rule rather than a wrong one.
    /// </summary>
    /// <param name="naming">What the diagnostic is about, from <see cref="DeclarationLocator"/>.</param>
    public static IReadOnlyList<RuleEntry> RulesFor(
        RulesManifest manifest,
        string ruleId,
        string message,
        string? sourceLine,
        string? profile,
        (NamingSymbolKind Kind, NamingFailure Failure)? naming)
    {
        RuleEntry[] rules = manifest.Rules.Where(r => r.Diagnostics.Contains(ruleId)).ToArray();
        bool tagged = ruleId == DeclarationLocator.NamingDiagnosticId && rules.Any(r => r.Naming is not null);
        if (tagged)
        {
            if (naming is not var (kind, failure))
            {
                return [];
            }

            string symbol = CamelCase(kind);
            string failureText = CamelCase(failure);
            rules = rules
                .Where(r => r.Naming is { } tags && tags.Symbols.Contains(symbol) && tags.Failures.Contains(failureText))
                .ToArray();
        }

        RuleEntry[] active = profile is null
            ? rules
            : rules.Where(r => !r.Severity.TryGetValue(profile, out string? severity) || severity != "none").ToArray();
        if (active.Length > 0)
        {
            rules = active;
        }

        if (!tagged && rules.Length > MaxRulesPerIssue)
        {
            string[] signal = Words(message).Except(Words(sourceLine ?? string.Empty)).ToArray();
            rules = rules.Where(r => TextWords(r.Text).Overlaps(signal)).ToArray();
        }

        string[] words = Words(message + " " + sourceLine);
        return rules
            .Select((rule, index) => (Rule: rule, Index: index, Score: words.Count(w => rule.Text.Contains(w, StringComparison.OrdinalIgnoreCase))))
            .OrderBy(r => tagged ? r.Rule.Naming!.Symbols.Count + r.Rule.Naming.Failures.Count : 0)
            .ThenByDescending(r => r.Score)
            .ThenBy(r => r.Index)
            .Select(r => r.Rule)
            .Take(MaxRulesPerIssue)
            .ToArray();
    }

    private static string CamelCase<T>(T value)
        where T : struct, Enum
    {
        string name = value.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    /// <summary>The text's words: its runs of ASCII letters, lower-cased.</summary>
    private static HashSet<string> TextWords(string text) => text
        .Split(text.Where(c => !char.IsAsciiLetter(c)).Distinct().ToArray(), StringSplitOptions.RemoveEmptyEntries)
        .Select(w => w.ToLowerInvariant())
        .ToHashSet(StringComparer.Ordinal);

    private static string[] Words(string message) => message
        .Split((char[])[' ', ':', ';', '=', '\'', '"', ',', '.', '(', ')', '`'], StringSplitOptions.RemoveEmptyEntries)
        .Select(w => w.ToLowerInvariant())
        .Where(w => w.Length >= 3 && w.All(char.IsAsciiLetter) && !IgnoredWords.Contains(w))
        .Distinct()
        .ToArray();
}
