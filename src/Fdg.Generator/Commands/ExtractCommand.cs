using System.Text;
using Fdg.Generator.Book;
using Fdg.Generator.Rules;

namespace Fdg.Generator.Commands;

/// <summary>
/// <c>extract --book &lt;OEBPS dir&gt; --chapter &lt;token&gt;</c>: writes a new rules skeleton.
/// With <c>--update</c>, re-extracts an existing rules file: the book's own fields are replaced and the mapping
/// of every rule is kept.
/// </summary>
public static class ExtractCommand
{
    private static readonly string[] AllowedOptions = { "book", "chapter", "rules" };
    private static readonly string[] AllowedFlags = { "update" };

    public static int Run(IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        return ToolErrors.Run("extract", error, () => Execute(args, output, error));
    }

    private static int Execute(IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        if (!CommandOptions.TryParse(args, AllowedOptions, out CommandOptions options, out string message, AllowedFlags)
            || !options.TryRequire("book", out string bookDir, out message)
            || !options.TryRequire("chapter", out string token, out message)
            || !RulesDirectory.TryResolve(options.Get("rules"), out string rulesDir, out message))
        {
            error.WriteLine(message);
            return 2;
        }

        ChapterMeta? chapter = RuleLoader.LoadMeta(rulesDir).Chapters.FirstOrDefault(c => c.Token == token);
        if (chapter is null)
        {
            error.WriteLine($"Chapter '{token}' is not listed in {Path.Combine(rulesDir, "meta.yaml")}.");
            return 2;
        }

        bool update = options.Has("update");
        string target = Path.Combine(rulesDir, chapter.Rules);
        if (!update && File.Exists(target))
        {
            error.WriteLine($"{target} already exists; extraction never overwrites.");
            return 1;
        }

        if (update && !File.Exists(target))
        {
            error.WriteLine($"{target} does not exist; --update only re-extracts an existing rules file.");
            return 1;
        }

        BookChapter extracted;
        try
        {
            extracted = BookParser.Parse(Path.Combine(bookDir, chapter.File), chapter.Token);
        }
        catch (Exception exception) when (exception is BookFormatException or IOException)
        {
            error.WriteLine(exception.Message);
            return 2;
        }

        if (!update)
        {
            Write(target, extracted.Rules);
            output.WriteLine($"Wrote {extracted.Rules.Count} rules ({extracted.IconCount} icons) to {target}.");
            return 0;
        }

        IReadOnlyList<Rule> existing = RuleLoader.LoadRules(target);
        if (!TryMerge(extracted.Rules, existing, out List<Rule> merged, out List<string> changed, out message))
        {
            error.WriteLine($"{target}: {message}");
            return 1;
        }

        Write(target, merged);
        output.WriteLine(changed.Count == 0
            ? $"Updated 0 of {merged.Count} rules."
            : $"Updated {changed.Count} of {merged.Count} rules: {string.Join(", ", changed)}");
        return 0;
    }

    /// <summary>
    /// Pairs the extracted rules with the existing ones by id and keeps the existing mapping; fails when the
    /// rule count, an id or a strength differs. <paramref name="changed"/> lists the rules whose book fields differ.
    /// </summary>
    private static bool TryMerge(
        IReadOnlyList<Rule> extracted,
        IReadOnlyList<Rule> existing,
        out List<Rule> merged,
        out List<string> changed,
        out string error)
    {
        merged = new List<Rule>();
        changed = new List<string>();
        if (extracted.Count != existing.Count)
        {
            error = $"the book yields {extracted.Count} rules but the file has {existing.Count}; --update needs the same rules.";
            return false;
        }

        Dictionary<string, Rule> byId = existing
            .GroupBy(r => r.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        List<string> missing = extracted.Where(r => !byId.ContainsKey(r.Id)).Select(r => r.Id).ToList();
        if (missing.Count > 0)
        {
            error = $"the file has no rule {string.Join(", ", missing)}; --update needs the same rules.";
            return false;
        }

        List<string> strengths = extracted
            .Where(r => byId[r.Id].Strength != r.Strength)
            .Select(r => $"{r.Id} ({byId[r.Id].Strength.ToText()} in the file, {r.Strength.ToText()} in the book)")
            .ToList();
        if (strengths.Count > 0)
        {
            error = $"the strength of {string.Join(", ", strengths)} differs; --update needs the same rules.";
            return false;
        }

        merged = extracted.Select(r => r with { Mapping = byId[r.Id].Mapping }).ToList();
        changed = extracted.Where(r => BookFields.Compare(r, byId[r.Id]).Count > 0).Select(r => r.Id).ToList();
        error = string.Empty;
        return true;
    }

    private static void Write(string target, IEnumerable<Rule> rules)
    {
        File.WriteAllText(target, RuleYaml.Serialize(rules), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
