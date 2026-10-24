using System.Text.RegularExpressions;
using Fdg.Generator.Sdk;

namespace Fdg.Generator.Rules;

/// <summary>
/// Checks the mapping half of every rule: ids, check kind against diagnostics and coverage, autofix against the
/// SDK's code fixes, analyzer, style flag, naming tags, language requirements, test profile, and (through <see cref="ConfigValidator"/>) config.
/// </summary>
public static class MappingValidator
{
    private const string AppendixWithStyle = "A";
    private const string TestProfileNote = "Test profile:";
    private const string NamingDiagnostic = "IDE1006";

    public static IReadOnlyList<CheckError> Validate(
        IReadOnlyList<ChapterRules> chapters,
        IReadOnlyDictionary<string, IReadOnlyList<string>> profiles,
        IReadOnlyDictionary<string, SdkDiagnostic> catalog)
    {
        var errors = new List<CheckError>();
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (ChapterRules chapter in chapters)
        {
            string file = chapter.Chapter.Rules;
            var idPattern = new Regex($"^FDG{Regex.Escape(chapter.Chapter.Token)}[0-9]{{3}}$", RegexOptions.CultureInvariant);
            foreach (Rule rule in chapter.Rules)
            {
                void Report(string message) => errors.Add(new CheckError(file, rule.Id, message));

                if (!idPattern.IsMatch(rule.Id))
                {
                    Report($"id must be FDG{chapter.Chapter.Token} followed by three digits in chapter {chapter.Chapter.Token}.");
                }

                if (!files.TryAdd(rule.Id, file))
                {
                    Report($"duplicate rule id (first defined in {files[rule.Id]}).");
                }

                bool appendixStyle = chapter.Chapter.Token == AppendixWithStyle;
                if (rule.Mapping.Style != appendixStyle)
                {
                    Report(appendixStyle
                        ? "style must be true for appendix A rules."
                        : "style is reserved for the appendix A coding style conventions.");
                }

                ValidateCheck(rule, catalog, Report);

                if (!chapter.Chapter.TestProfileOn && rule.Mapping.Severity.Test != SeverityLevel.None
                    && rule.Mapping.Notes?.Contains(TestProfileNote, StringComparison.Ordinal) != true)
                {
                    Report($"chapter {chapter.Chapter.Token} is off in the test profile; " +
                        $"set severity.test to none or explain it in notes with \"{TestProfileNote}\".");
                }

                if (rule.Mapping.Naming is not null && !rule.Mapping.Check.Diagnostics.Contains(NamingDiagnostic))
                {
                    Report($"naming tags select among the rules of {NamingDiagnostic}, so check.diagnostics must list it.");
                }

                if (rule.Mapping.Alternative is not null && rule.Mapping.MinLanguage is null)
                {
                    Report("alternative describes what to write below minLanguage; set minLanguage too.");
                }
            }
        }

        errors.AddRange(ConfigValidator.ValidateRules(chapters, profiles));
        return errors;
    }

    private static void ValidateCheck(Rule rule, IReadOnlyDictionary<string, SdkDiagnostic> catalog, Action<string> report)
    {
        RuleMapping mapping = rule.Mapping;
        CheckInfo check = mapping.Check;
        string kind = RuleYaml.EnumText(check.Kind);

        if (mapping.Analyzer is not null && check.Kind != CheckKind.Fdg)
        {
            report($"analyzer is only used by fdg rules, but check.kind is {kind}.");
        }

        if (check.Kind is CheckKind.Judgment or CheckKind.None)
        {
            if (check.Diagnostics.Count > 0)
            {
                report($"{kind} rules list no diagnostics, but found {string.Join(", ", check.Diagnostics)}.");
            }

            if (check.Coverage != CoverageLevel.None)
            {
                report($"{kind} rules have coverage none.");
            }

            if (mapping.Autofix != AutofixKind.No)
            {
                report($"{kind} rules have autofix no.");
            }

            return;
        }

        if (check.Coverage == CoverageLevel.None)
        {
            report($"{kind} rules need coverage full or partial.");
        }

        IEnumerable<string> sdkDiagnostics = check.Diagnostics;
        if (check.Kind == CheckKind.Fdg)
        {
            if (!check.Diagnostics.Contains(rule.Id))
            {
                report($"fdg rules list their own diagnostic {rule.Id} in check.diagnostics.");
            }

            if (mapping.Analyzer is null)
            {
                report("fdg rules need analyzer.message and analyzer.category.");
            }

            sdkDiagnostics = sdkDiagnostics.Where(id => id != rule.Id);
        }
        else if (check.Diagnostics.Count == 0)
        {
            report("sdk rules need at least one diagnostic in check.diagnostics.");
        }

        var known = new List<SdkDiagnostic>();
        foreach (string id in sdkDiagnostics)
        {
            if (id.StartsWith("FDG", StringComparison.Ordinal))
            {
                report($"{id} is not this rule's own diagnostic; only SDK diagnostics may be listed besides it.");
            }
            else if (catalog.TryGetValue(id, out SdkDiagnostic? diagnostic))
            {
                known.Add(diagnostic);
            }
            else
            {
                report($"diagnostic {id} does not exist in the installed SDK's C# analyzers.");
            }
        }

        if (check.Kind == CheckKind.Sdk && mapping.Autofix != AutofixKind.No
            && known.Count == check.Diagnostics.Count && !known.Any(d => d.HasCodeFix))
        {
            report($"autofix {RuleYaml.EnumText(mapping.Autofix)} needs a code fix, but none of " +
                $"{string.Join(", ", check.Diagnostics)} has one in the installed SDK.");
        }
    }
}
