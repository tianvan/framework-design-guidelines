using System.Text;
using Fdg.Generator.Rules;

namespace Fdg.Generator.Emit;

/// <summary>
/// Writes <c>docs/coverage.md</c>: rule counts per chapter, where the book's non-guideline text went, and the parts
/// of the book without rules.
/// </summary>
public static class CoverageEmitter
{
    public static string Emit(GeneratorInput input)
    {
        var text = new StringBuilder();
        text.Append(Header.Markdown).Append("\n\n");
        text.Append("# Coverage\n\n");
        text.Append("Rules per chapter, by how they are checked. `sdk` rules map to SDK analyzers, `fdg` rules to this ");
        text.Append("project's analyzers, `judgment` rules are left to the AI and the reviewer, and `none` rules ");
        text.Append("only set formatting options.\n\n");
        text.Append("| Chapter | Rules | sdk | fdg | judgment | none | verified |\n");
        text.Append("| --- | ---: | ---: | ---: | ---: | ---: | ---: |\n");
        foreach (RuleFile file in input.Files)
        {
            text.Append(Row($"{file.Chapter} {file.Title}", file.Rules)).Append('\n');
        }

        text.Append(Row("Total", input.Rules.ToList())).Append('\n');

        text.Append("\n## Non-guideline content\n\n");
        text.Append("Book text that is not a guideline, and where it went: kept as the rationale of a rule, ");
        text.Append("kept as background, or dropped as obsolete.\n");
        foreach (RuleFile file in input.Files)
        {
            text.Append($"\n### {RuleText.ChapterLabel(file.Chapter)}: {file.Title}\n\n");
            if (file.NonGuideline.Count == 0)
            {
                text.Append("Nothing recorded yet.\n");
                continue;
            }

            AppendEntries(text, file.NonGuideline);
        }

        text.Append("\n## Other parts of the book\n\n");
        text.Append("The parts of the book outside the chapters and appendices above, which carry no rules, ");
        text.Append("and where their text went.\n");
        foreach (OtherContent part in input.Meta.OtherContent)
        {
            text.Append($"\n### {part.Title}\n\n");
            AppendEntries(text, part.Entries);
        }

        return text.ToString();
    }

    private static void AppendEntries(StringBuilder text, IEnumerable<NonGuidelineEntry> entries)
    {
        foreach (NonGuidelineEntry entry in entries)
        {
            text.Append($"- **{entry.Section}** ({RuleYaml.EnumText(entry.Disposition)}): {entry.Note}\n");
        }
    }

    private static string Row(string label, IReadOnlyCollection<Rule> rules)
    {
        int Count(CheckKind kind) => rules.Count(r => r.Mapping.Check.Kind == kind);

        return $"| {label} | {rules.Count} | {Count(CheckKind.Sdk)} | {Count(CheckKind.Fdg)} | {Count(CheckKind.Judgment)} " +
            $"| {Count(CheckKind.None)} | {rules.Count(r => r.Mapping.Status == RuleStatus.Verified)} |";
    }
}
