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
    /// The rules a diagnostic checks under <paramref name="profile"/>, most relevant first. One diagnostic such as
    /// IDE1006 can check many rules; the ones sharing more words with the message and the flagged line (for
    /// "Missing prefix" on a private field, the private-field prefix rule) come first, otherwise the manifest's
    /// order is kept. When there are more than <see cref="MaxRulesPerIssue"/>, only rules whose text has a word of
    /// the message that is not in the flagged line (such as "prefix", but not the flagged name) are kept, so a
    /// diagnostic like "must begin with upper case characters" quotes no rule rather than a wrong one.
    /// </summary>
    public static IReadOnlyList<RuleEntry> RulesFor(
        RulesManifest manifest,
        string ruleId,
        string message,
        string? sourceLine,
        string? profile)
    {
        RuleEntry[] rules = manifest.Rules.Where(r => r.Diagnostics.Contains(ruleId)).ToArray();
        RuleEntry[] active = profile is null
            ? rules
            : rules.Where(r => !r.Severity.TryGetValue(profile, out string? severity) || severity != "none").ToArray();
        if (active.Length > 0)
        {
            rules = active;
        }

        if (rules.Length > MaxRulesPerIssue)
        {
            string[] signal = Words(message).Except(Words(sourceLine ?? string.Empty)).ToArray();
            rules = rules.Where(r => TextWords(r.Text).Overlaps(signal)).ToArray();
        }

        string[] words = Words(message + " " + sourceLine);
        return rules
            .Select((rule, index) => (Rule: rule, Index: index, Score: words.Count(w => rule.Text.Contains(w, StringComparison.OrdinalIgnoreCase))))
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.Index)
            .Select(r => r.Rule)
            .ToArray();
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
