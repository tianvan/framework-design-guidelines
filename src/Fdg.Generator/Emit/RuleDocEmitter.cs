using System.Text;
using Fdg.Generator.Rules;

namespace Fdg.Generator.Emit;

/// <summary>Writes <c>docs/rules/&lt;ID&gt;.md</c>, the page each diagnostic's help link points to.</summary>
public static class RuleDocEmitter
{
    public static IReadOnlyList<GeneratedFile> Emit(GeneratorInput input)
    {
        return input.Rules.Select(rule => new GeneratedFile($"docs/rules/{rule.Id}.md", Page(rule))).ToList();
    }

    private static string Page(Rule rule)
    {
        RuleMapping mapping = rule.Mapping;
        var text = new StringBuilder();
        text.Append(Header.Markdown).Append("\n\n");
        text.Append($"# {rule.Id}\n\n");
        text.Append($"> {EscapeLiterals(RuleText.Statement(rule))}\n\n");
        text.Append($"Framework Design Guidelines, 3rd edition, section {RuleText.EscapeMarkdown(rule.Section)}.");
        if (rule.Context is not null)
        {
            string context = EscapeLiterals(rule.Context);
            text.Append(rule.Strength.IsVerdict() ? $" Can cause: {context}." : $" Applies to: {context}.");
        }

        text.Append("\n\n");
        if (mapping.Style)
        {
            text.Append("This is a coding style convention. The book treats the appendix A conventions as suggestions, so you can override it.\n\n");
        }

        text.Append("## How it is checked\n\n");
        text.Append($"- Check: {RuleYaml.EnumText(mapping.Check.Kind)}");
        if (mapping.Check.Kind is CheckKind.Sdk or CheckKind.Fdg)
        {
            text.Append($" ({RuleYaml.EnumText(mapping.Check.Coverage)} coverage)");
        }

        text.Append('\n');
        if (mapping.Check.Diagnostics.Count > 0)
        {
            text.Append($"- Diagnostics: {string.Join(", ", mapping.Check.Diagnostics)}\n");
        }

        text.Append($"- Automatic fix: {AutofixText(mapping.Autofix)}\n");
        text.Append("- Severity: ");
        text.Append(string.Join(", ", ProfileFile.Names.Select(p => $"{p} {RuleText.Level(RuleText.Severity(rule, p))}")));
        text.Append('\n');
        string? tag = RuleText.LanguageTag(rule);
        if (tag is not null)
        {
            text.Append($"- Language: {tag}");
            text.Append(mapping.Alternative is null ? "\n" : $" (before that: {mapping.Alternative})\n");
        }

        text.Append($"- Status: {RuleYaml.EnumText(mapping.Status)}\n");
        if (mapping.Analyzer is not null)
        {
            text.Append($"\nMessage: {EscapeLiterals(mapping.Analyzer.Message)}\n");
        }

        if (mapping.Notes is not null)
        {
            text.Append($"\n{EscapeLiterals(mapping.Notes)}\n");
        }

        if (mapping.Config.Count > 0)
        {
            text.Append("\n## Options\n\n```ini\n");
            text.Append(string.Join("\n", mapping.Config)).Append("\n```\n");
        }

        if (rule.Body.Count > 0)
        {
            text.Append("\n## From the book\n\n");
            IReadOnlyList<Block> body = rule.Body
                .Select(b => b.Kind == BlockKind.Code ? b : b with { Text = EscapeLiterals(b.Text) })
                .ToList();
            text.Append(RuleText.RenderBody(body)).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>
    /// Escapes the characters of book text and notes that GitHub would otherwise read as Markdown: a <c>&lt;</c> that
    /// could open an HTML tag, such as <c>&lt;project root&gt;</c> or <c>List&lt;T&gt;</c>, and a <c>_</c> that could
    /// open or close emphasis, which is any <c>_</c> that is not between two letters or digits. Code spans, fenced
    /// code, link destinations, backslash escapes and the extractor's own <c>&lt;br&gt;</c> and <c>&lt;sup&gt;</c>
    /// markup are kept as they are. Skill references are not escaped: the AI reads them as text.
    /// </summary>
    internal static string EscapeLiterals(string markdown)
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
            if (line.TrimStart(' ', '>').StartsWith("```", StringComparison.Ordinal))
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
                string? tag = KeptTags.FirstOrDefault(t => string.CompareOrdinal(line, i, t, 0, t.Length) == 0);
                next = i + (tag?.Length ?? 1);
                text.Append(tag ?? "&lt;");
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

    private static readonly string[] KeptTags = { "<br>", "<sup>", "</sup>" };

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

    private static string AutofixText(AutofixKind autofix)
    {
        return autofix switch
        {
            AutofixKind.Yes => "yes",
            AutofixKind.No => "no",
            _ => "not applied automatically (the fix can break callers)",
        };
    }
}
