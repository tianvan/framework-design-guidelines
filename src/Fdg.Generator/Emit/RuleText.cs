using Fdg.Generator.Rules;

namespace Fdg.Generator.Emit;

/// <summary>Small text conventions shared by the emitters.</summary>
internal static class RuleText
{
    /// <summary>The guideline as the book states it: strength followed by the sentence.</summary>
    public static string Statement(Rule rule)
    {
        return $"{rule.Strength.ToText()} {rule.Text}";
    }

    /// <summary>The version tag of a rule that needs newer syntax, such as <c>[C# 9+]</c>; <c>null</c> when there is none.</summary>
    public static string? LanguageTag(Rule rule)
    {
        string? version = rule.Mapping.MinLanguage;
        if (version is null)
        {
            return null;
        }

        return $"[C# {(version.EndsWith(".0", StringComparison.Ordinal) ? version[..^2] : version)}+]";
    }

    public static string Level(SeverityLevel level)
    {
        return RuleYaml.EnumText(level);
    }

    public static SeverityLevel Severity(Rule rule, string profile)
    {
        ProfileSeverity severity = rule.Mapping.Severity;
        return profile switch
        {
            "library" => severity.Library,
            "application" => severity.Application,
            "test" => severity.Test,
            _ => throw new GenerationException($"Unknown profile '{profile}'."),
        };
    }

    /// <summary>"Chapter 3" for numbered chapters, "Appendix A" for lettered ones.</summary>
    public static string ChapterLabel(string token)
    {
        return char.IsDigit(token[0]) ? $"Chapter {token}" : $"Appendix {token}";
    }

    /// <summary>The file stem shared by a chapter's skill reference files, such as <c>ch03-naming</c>.</summary>
    public static string FileStem(RuleFile file)
    {
        return file.RulesFile.Length > 0
            ? Path.GetFileNameWithoutExtension(file.RulesFile)
            : "ch" + file.Chapter;
    }

    /// <summary>The section number, the first word of a rule's <c>Section</c>, such as <c>5.2.1</c>.</summary>
    public static string SectionNumber(Rule rule)
    {
        string section = rule.Section.Trim();
        int space = section.IndexOf(' ', StringComparison.Ordinal);
        return space < 0 ? section : section[..space];
    }

    /// <summary>
    /// Section text is plain text and can hold generic names such as <c>IComparable&lt;T&gt;</c>; escapes the
    /// <c>&lt;</c> so Markdown does not swallow <c>&lt;T&gt;</c> as an HTML tag.
    /// </summary>
    public static string EscapeMarkdown(string text)
    {
        return text.Replace("<", "&lt;", StringComparison.Ordinal);
    }

    /// <summary>Renders a rule's body blocks as Markdown, separated by blank lines.</summary>
    public static string RenderBody(IReadOnlyList<Block> body)
    {
        return string.Join("\n\n", body.Select(RenderBlock));
    }

    private static string RenderBlock(Block block)
    {
        switch (block.Kind)
        {
            case BlockKind.Code:
                return $"```csharp\n{block.Text}\n```";
            case BlockKind.Annotation:
                string label = block.Author is null ? "Note" : block.Author;
                string[] lines = block.Text.Split('\n');
                lines[0] = $"**{label}:** {lines[0]}";
                return string.Join("\n", lines.Select(l => l.Length == 0 ? ">" : "> " + l));
            default:
                return block.Text;
        }
    }
}
