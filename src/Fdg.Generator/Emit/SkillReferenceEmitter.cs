using System.Text;
using Fdg.Generator.Rules;

namespace Fdg.Generator.Emit;

/// <summary>
/// Writes the <c>fdg-write</c> skill's reference files: per chapter, a one-line-per-rule index of what tools cannot
/// check, and a second file with the book's explanations and examples for those rules. A chapter can be split by
/// section into several such pairs (<c>references</c> in <c>meta.yaml</c>); no file may exceed
/// <see cref="MaxReferenceBytes"/>.
/// </summary>
public static class SkillReferenceEmitter
{
    /// <summary>The size limit of every reference file, in UTF-8 bytes.</summary>
    public const int MaxReferenceBytes = 32768;

    private const string ReferencesDirectory = "plugins/framework-design-guidelines/skills/fdg-write/references/";

    public static IReadOnlyList<GeneratedFile> Emit(GeneratorInput input)
    {
        var files = new List<GeneratedFile>();
        var stems = new Dictionary<string, string>();
        foreach (RuleFile file in input.Files)
        {
            foreach (ReferencePart part in Parts(file))
            {
                if (!stems.TryAdd(part.Stem, RuleText.ChapterLabel(file.Chapter)))
                {
                    throw new GenerationException(
                        $"{RuleText.ChapterLabel(file.Chapter)}: reference file '{part.Stem}' is already used by {stems[part.Stem]}.");
                }

                files.Add(Checked(part.Stem + ".md", Index(file, part)));
                files.Add(Checked("examples/" + part.Stem + ".md", Examples(file, part)));
            }
        }

        return files;
    }

    private static GeneratedFile Checked(string relativePath, string content)
    {
        int bytes = Encoding.UTF8.GetByteCount(content);
        if (bytes > MaxReferenceBytes)
        {
            throw new GenerationException(
                $"references/{relativePath} is {bytes} bytes; the limit is {MaxReferenceBytes}. Split the chapter with references in meta.yaml.");
        }

        return new GeneratedFile(ReferencesDirectory + relativePath, content);
    }

    /// <summary>The chapter's pairs of reference files: one for the whole chapter, or one per <c>references</c> split.</summary>
    private static IReadOnlyList<ReferencePart> Parts(RuleFile file)
    {
        if (file.References.Count == 0)
        {
            return new[] { new ReferencePart(RuleText.FileStem(file), file.Rules, null) };
        }

        string[] duplicates = file.References.GroupBy(r => r.File).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
        if (duplicates.Length > 0)
        {
            throw new GenerationException($"{RuleText.ChapterLabel(file.Chapter)}: reference split '{duplicates[0]}' is listed twice.");
        }

        var rulesBySplit = file.References.ToDictionary(r => r, _ => new List<Rule>());
        foreach (Rule rule in file.Rules)
        {
            string number = RuleText.SectionNumber(rule);
            // The most specific prefix wins, so a split can list the chapter number for the rules before its first section.
            int MatchLength(ReferenceSplit split) =>
                split.Sections.Where(prefix => IsInSection(number, prefix)).Select(prefix => prefix.Length).DefaultIfEmpty(-1).Max();
            int best = file.References.Max(MatchLength);
            ReferenceSplit[] matches = best < 0 ? [] : file.References.Where(r => MatchLength(r) == best).ToArray();
            if (matches.Length == 0)
            {
                throw new GenerationException(
                    $"{RuleText.ChapterLabel(file.Chapter)}: rule {rule.Id} (section {number}) matches no reference split.");
            }

            if (matches.Length > 1)
            {
                throw new GenerationException(
                    $"{RuleText.ChapterLabel(file.Chapter)}: rule {rule.Id} (section {number}) matches more than one reference split ({string.Join(", ", matches.Select(m => m.File))}).");
            }

            rulesBySplit[matches[0]].Add(rule);
        }

        foreach (ReferenceSplit split in file.References)
        {
            if (rulesBySplit[split].Count == 0)
            {
                throw new GenerationException($"{RuleText.ChapterLabel(file.Chapter)}: reference split '{split.File}' matches no rule.");
            }
        }

        return file.References.Select(r => new ReferencePart(r.File, rulesBySplit[r], SectionRange(r))).ToList();
    }

    /// <summary>Whether the section number is the prefix itself or a section below it: <c>5.1</c> matches <c>5.1.2</c>, not <c>5.10</c>.</summary>
    private static bool IsInSection(string number, string prefix)
    {
        return number == prefix || number.StartsWith(prefix + ".", StringComparison.Ordinal);
    }

    /// <summary>The first and last section of the split in numeric order, however <c>meta.yaml</c> lists them.</summary>
    private static string SectionRange(ReferenceSplit split)
    {
        string[] sorted = split.Sections.Order(SectionOrder.Instance).ToArray();
        string first = sorted[0];
        string last = sorted[^1];
        return first == last ? first : $"{first}–{last}";
    }

    /// <summary>Orders section numbers by their dotted parts, so <c>5.2</c> comes before <c>5.10</c>.</summary>
    private sealed class SectionOrder : IComparer<string>
    {
        public static readonly SectionOrder Instance = new();

        public int Compare(string? x, string? y)
        {
            string[] a = (x ?? string.Empty).Split('.');
            string[] b = (y ?? string.Empty).Split('.');
            for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
            {
                int result = int.TryParse(a[i], out int left) && int.TryParse(b[i], out int right)
                    ? left.CompareTo(right)
                    : string.CompareOrdinal(a[i], b[i]);
                if (result != 0)
                {
                    return result;
                }
            }

            return a.Length.CompareTo(b.Length);
        }
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

    private static string Index(RuleFile file, ReferencePart part)
    {
        var text = new StringBuilder();
        text.Append(Header.Markdown).Append("\n\n");
        text.Append($"# {Title(file, part)}\n\n");
        text.Append("Rules that no analyzer enforces (judgment), and CONSIDER rules that analyzers only suggest. ");
        text.Append("Open the examples file for the explanation and code the book gives with each rule.\n");
        if (part.Rules.Any(r => r.Mapping.Style))
        {
            text.Append("\nThese are coding style conventions. The book calls them suggestions, not requirements.\n");
        }

        foreach (IGrouping<string, Rule> section in part.Rules.Where(IsListed).GroupBy(r => r.Section))
        {
            text.Append($"\n## {RuleText.EscapeMarkdown(section.Key)}\n\n");
            foreach (Rule rule in section)
            {
                text.Append(Line(rule, part.Stem)).Append('\n');
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

    private static string Examples(RuleFile file, ReferencePart part)
    {
        var text = new StringBuilder();
        text.Append(Header.Markdown).Append("\n\n");
        text.Append($"# {Title(file, part)} — explanations and examples\n");
        foreach (Rule rule in part.Rules.Where(r => IsListed(r) && r.Body.Count > 0))
        {
            text.Append($"\n## {rule.Id}\n\n");
            text.Append(RuleText.Statement(rule)).Append("\n\n");
            text.Append(RuleText.RenderBody(rule.Body)).Append('\n');
        }

        return text.ToString();
    }

    private static string Title(RuleFile file, ReferencePart part)
    {
        string title = $"{RuleText.ChapterLabel(file.Chapter)}: {file.Title}";
        return part.Sections is null ? title : $"{title} — {part.Sections}";
    }

    /// <summary>One pair of reference files: its file stem, its rules, and the section range for the title (<c>null</c> when unsplit).</summary>
    private sealed record ReferencePart(string Stem, IReadOnlyList<Rule> Rules, string? Sections);
}
