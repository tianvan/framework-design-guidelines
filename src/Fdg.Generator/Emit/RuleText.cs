using System.Text;
using Fdg.Generator.Rules;

namespace Fdg.Generator.Emit;

/// <summary>Small text conventions shared by the emitters.</summary>
internal static class RuleText
{
    /// <summary>
    /// The guideline as the book states it: strength followed by the sentence. An appendix D verdict is set off
    /// from its heading text by a colon, since the two do not read as one sentence.
    /// </summary>
    public static string Statement(Rule rule)
    {
        return $"{rule.Strength.ToText()}{(rule.Strength.IsVerdict() ? ": " : " ")}{rule.Text}";
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

    /// <summary>
    /// Escapes the characters of book text and notes that GitHub would otherwise read as Markdown: a <c>&lt;</c> that
    /// could open an HTML tag, such as <c>&lt;project root&gt;</c> or <c>List&lt;T&gt;</c>, and a <c>_</c> that could
    /// open or close emphasis, which is any <c>_</c> that is not between two letters or digits. Code spans, code
    /// fenced with <c>```</c> or <c>~~~</c>, link destinations, autolinks such as <c>&lt;https://...&gt;</c>, backslash
    /// escapes and the extractor's own <c>&lt;br&gt;</c> and <c>&lt;sup&gt;</c> markup are kept as they are, so
    /// escaping twice gives the same text. Skill references are not escaped: the AI reads them as text.
    /// </summary>
    public static string EscapeLiterals(string markdown)
    {
        var text = new StringBuilder(markdown.Length);
        bool inFence = false;
        string[] lines = markdown.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                text.Append('\n');
            }

            string line = lines[i];
            string start = line.TrimStart(' ', '>');
            if (start.StartsWith("```", StringComparison.Ordinal) || start.StartsWith("~~~", StringComparison.Ordinal))
            {
                inFence = !inFence;
                text.Append(line);
            }
            else if (inFence)
            {
                text.Append(line);
            }
            else
            {
                EscapeInline(text, line);
            }
        }

        return text.ToString();
    }

    private static void EscapeInline(StringBuilder text, string line)
    {
        int i = 0;
        while (i < line.Length)
        {
            char c = line[i];
            int next = i + 1;
            if (c == '`')
            {
                // A code span ends at the next run of as many backticks; an unmatched run is literal text.
                int run = RunLength(line, i, '`');
                int close = FindRun(line, i + run, '`', run);
                next = close < 0 ? i + run : close + run;
                text.Append(line, i, next - i);
            }
            else if (c == '\\' && next < line.Length && IsAsciiPunctuation(line[next]))
            {
                next = i + 2;
                text.Append(line, i, 2);
            }
            else if (c == ']' && next < line.Length && line[next] == '(' && line.IndexOf(')', next) is >= 0 and int end)
            {
                next = end + 1;
                text.Append(line, i, next - i);
            }
            else if (c == '<')
            {
                string? kept = KeptTags.FirstOrDefault(t => string.CompareOrdinal(line, i, t, 0, t.Length) == 0)
                    ?? Autolink(line, i);
                next = i + (kept?.Length ?? 1);
                text.Append(kept ?? "&lt;");
            }
            else if (c == '_' && !(i > 0 && char.IsLetterOrDigit(line[i - 1]) && next < line.Length
                && char.IsLetterOrDigit(line[next])))
            {
                text.Append("\\_");
            }
            else
            {
                text.Append(c);
            }

            i = next;
        }
    }

    private static readonly string[] KeptTags = { "<br>", "<br/>", "<br />", "<sup>", "</sup>" };

    private static readonly string[] AutolinkSchemes = { "http://", "https://", "mailto:" };

    /// <summary>The CommonMark autolink that starts at <paramref name="start"/>, such as <c>&lt;https://...&gt;</c>, or null.</summary>
    private static string? Autolink(string line, int start)
    {
        if (!AutolinkSchemes.Any(s => string.Compare(line, start + 1, s, 0, s.Length, StringComparison.OrdinalIgnoreCase) == 0))
        {
            return null;
        }

        int end = start + 1;
        while (end < line.Length && line[end] is not ('>' or '<' or ' '))
        {
            end++;
        }

        return end < line.Length && line[end] == '>' ? line.Substring(start, end - start + 1) : null;
    }

    /// <summary>The characters a backslash escapes in CommonMark.</summary>
    private static bool IsAsciiPunctuation(char c)
    {
        return c <= '~' && (char.IsPunctuation(c) || char.IsSymbol(c));
    }

    private static int RunLength(string line, int start, char c)
    {
        int end = start;
        while (end < line.Length && line[end] == c)
        {
            end++;
        }

        return end - start;
    }

    /// <summary>The start of the next run of exactly <paramref name="length"/> <paramref name="c"/> characters, or -1.</summary>
    private static int FindRun(string line, int start, char c, int length)
    {
        int i = start;
        while (i < line.Length)
        {
            int run = RunLength(line, i, c);
            if (run == length)
            {
                return i;
            }

            i += Math.Max(run, 1);
        }

        return -1;
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
