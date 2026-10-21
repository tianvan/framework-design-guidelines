using System.Text;
using Fdg.Generator.Book;
using Fdg.Generator.Rules;

namespace Fdg.Generator.Commands;

/// <summary><c>extract --book &lt;OEBPS dir&gt; --chapter &lt;token&gt;</c>: writes a new rules skeleton.</summary>
public static class ExtractCommand
{
    private static readonly string[] AllowedOptions = { "book", "chapter", "rules" };

    public static int Run(IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        if (!CommandOptions.TryParse(args, AllowedOptions, out CommandOptions options, out string message)
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

        string target = Path.Combine(rulesDir, chapter.Rules);
        if (File.Exists(target))
        {
            error.WriteLine($"{target} already exists; extraction never overwrites.");
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

        File.WriteAllText(target, RuleYaml.Serialize(extracted.Rules), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        output.WriteLine($"Wrote {extracted.Rules.Count} rules ({extracted.IconCount} icons) to {target}.");
        return 0;
    }
}
