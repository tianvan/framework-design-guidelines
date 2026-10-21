using System.Text;
using Fdg.Generator.Rules;

namespace Fdg.Generator.Emit;

/// <summary>
/// Writes the <c>fdg-write</c> skill's reference files: per chapter, a one-line-per-rule index of what tools cannot
/// check, and a second file with the book's explanations and examples for those rules.
/// </summary>
public static class SkillReferenceEmitter
{
    private const string ReferencesDirectory = "plugins/framework-design-guidelines/skills/fdg-write/references/";

    public static IReadOnlyList<GeneratedFile> Emit(GeneratorInput input)
    {
        var files = new List<GeneratedFile>();
        foreach (RuleFile file in input.Files)
        {
            string stem = RuleText.FileStem(file);
            files.Add(new GeneratedFile(ReferencesDirectory + stem + ".md", Index(file, stem)));
            files.Add(new GeneratedFile(ReferencesDirectory + "examples/" + stem + ".md", Examples(file)));
        }

        return files;
    }

    /// <summary>
    /// Whether the AI must be told about the rule: tools cannot check it, or they only suggest (a CONSIDER rule is
    /// <c>suggestion</c> by default and Stop does not act on suggestions).
    /// </summary>
    public static bool IsListed(Rule rule)
    {
        CheckKind kind = rule.Mapping.Check.Kind;
        return kind == CheckKind.Judgment
            || (rule.Strength == Strength.Consider && kind is CheckKind.Sdk or CheckKind.Fdg);
    }

    private static string Index(RuleFile file, string stem)
    {
        var text = new StringBuilder();
        text.Append(Header.Markdown).Append("\n\n");
        text.Append($"# {RuleText.ChapterLabel(file.Chapter)}: {file.Title}\n\n");
        text.Append("Rules that no analyzer enforces (judgment), and CONSIDER rules that analyzers only suggest. ");
        text.Append("Open the examples file for the explanation and code the book gives with each rule.\n");
        if (file.Rules.Any(r => r.Mapping.Style))
        {
            text.Append("\nThese are coding style conventions. The book calls them suggestions, not requirements.\n");
        }

        foreach (IGrouping<string, Rule> section in file.Rules.Where(IsListed).GroupBy(r => r.Section))
        {
            text.Append($"\n## {section.Key}\n\n");
            foreach (Rule rule in section)
            {
                text.Append(Line(rule, stem)).Append('\n');
            }
        }

        return text.ToString();
    }

    private static string Line(Rule rule, string stem)
    {
        var line = new StringBuilder($"- **{rule.Id}** {RuleText.Statement(rule)}");
        if (rule.Context is not null)
        {
            line.Append($" (context: {rule.Context})");
        }

        string? tag = RuleText.LanguageTag(rule);
        if (tag is not null)
        {
            line.Append(' ').Append(tag);
        }

        if (rule.Mapping.Alternative is not null)
        {
            line.Append($" (alt: {rule.Mapping.Alternative})");
        }

        if (rule.Body.Count > 0)
        {
            line.Append($" — examples: examples/{stem}.md#{rule.Id.ToLowerInvariant()}");
        }

        return line.ToString();
    }

    private static string Examples(RuleFile file)
    {
        var text = new StringBuilder();
        text.Append(Header.Markdown).Append("\n\n");
        text.Append($"# {RuleText.ChapterLabel(file.Chapter)}: {file.Title} — explanations and examples\n");
        foreach (Rule rule in file.Rules.Where(r => IsListed(r) && r.Body.Count > 0))
        {
            text.Append($"\n## {rule.Id}\n\n");
            text.Append(RuleText.Statement(rule)).Append("\n\n");
            text.Append(RuleText.RenderBody(rule.Body)).Append('\n');
        }

        return text.ToString();
    }
}
