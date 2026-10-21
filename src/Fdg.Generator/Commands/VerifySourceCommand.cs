using Fdg.Generator.Book;
using Fdg.Generator.Rules;

namespace Fdg.Generator.Commands;

/// <summary>
/// <c>verify-source --book &lt;OEBPS dir&gt;</c>: re-extracts every listed chapter and compares the
/// book's own fields (everything but the mapping) with the rule files.
/// </summary>
public static class VerifySourceCommand
{
    private static readonly string[] AllowedOptions = { "book", "rules" };

    public static int Run(IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        if (!CommandOptions.TryParse(args, AllowedOptions, out CommandOptions options, out string message)
            || !options.TryRequire("book", out string bookDir, out message)
            || !RulesDirectory.TryResolve(options.Get("rules"), out string rulesDir, out message))
        {
            error.WriteLine(message);
            return 2;
        }

        bool failed = false;
        foreach (ChapterMeta chapter in RuleLoader.LoadMeta(rulesDir).Chapters)
        {
            string rulesPath = Path.Combine(rulesDir, chapter.Rules);
            if (!File.Exists(rulesPath))
            {
                error.WriteLine($"Chapter {chapter.Token}: {rulesPath} does not exist.");
                failed = true;
                continue;
            }

            BookChapter extracted;
            try
            {
                extracted = BookParser.Parse(Path.Combine(bookDir, chapter.File), chapter.Token);
            }
            catch (Exception exception) when (exception is BookFormatException or IOException)
            {
                error.WriteLine($"Chapter {chapter.Token}: {exception.Message}");
                failed = true;
                continue;
            }

            List<string> differences = Compare(extracted.Rules, RuleLoader.LoadRules(rulesPath));
            if (differences.Count == 0)
            {
                output.WriteLine($"Chapter {chapter.Token} ({chapter.File}): {extracted.Rules.Count} rules match the book.");
                continue;
            }

            failed = true;
            foreach (string difference in differences)
            {
                error.WriteLine($"Chapter {chapter.Token}: {difference}");
            }
        }

        return failed ? 1 : 0;
    }

    private static List<string> Compare(IReadOnlyList<Rule> book, IReadOnlyList<Rule> file)
    {
        var differences = new List<string>();
        if (book.Count != file.Count)
        {
            differences.Add($"the book yields {book.Count} rules but the file has {file.Count}.");
        }

        foreach ((Rule expected, Rule actual) in book.Zip(file))
        {
            CompareField(differences, expected.Id, "id", expected.Id, actual.Id);
            CompareField(differences, expected.Id, "strength", expected.Strength, actual.Strength);
            CompareField(differences, expected.Id, "text", expected.Text, actual.Text);
            CompareField(differences, expected.Id, "context", expected.Context, actual.Context);
            CompareField(differences, expected.Id, "section", expected.Section, actual.Section);
            CompareField(differences, expected.Id, "anchor", expected.Anchor, actual.Anchor);
            if (!expected.Body.SequenceEqual(actual.Body))
            {
                differences.Add($"{expected.Id}: field 'body' differs from the book.");
            }
        }

        return differences;
    }

    private static void CompareField<T>(List<string> differences, string id, string field, T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            differences.Add($"{id}: field '{field}' differs from the book (expected '{expected}', found '{actual}').");
        }
    }
}
