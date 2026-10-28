using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Fdg.Generator.Rules;

namespace Fdg.Generator.Book;

/// <summary>
/// Checks that the <c>nonGuideline</c> and <c>otherContent</c> notes of <c>meta.yaml</c> are in our own words: no note
/// may share a run of <see cref="CopiedRunLength"/> consecutive words with the xhtml file it describes. Words are compared
/// case-insensitively, without their punctuation; the book's markup is dropped and its entities decoded.
/// </summary>
public static class NoteCopyValidator
{
    /// <summary>The length of a run of words that counts as copied.</summary>
    public const int CopiedRunLength = 8;

    private static readonly Regex Tag = new("<[^>]*>", RegexOptions.CultureInvariant);

    public static IReadOnlyList<CheckError> Validate(MetaFile meta, string bookDir)
    {
        IEnumerable<(string Note, string Source, NonGuidelineEntry Entry)> notes = meta.Chapters
            .SelectMany(c => c.NonGuideline.Select(e => ($"nonGuideline note of chapter {c.Token}", c.File, e)))
            .Concat(meta.OtherContent.SelectMany(p => p.Entries.Select(e => ($"otherContent note of {p.Source}", p.Source, e))));

        var runsBySource = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var errors = new List<CheckError>();
        foreach ((string note, string source, NonGuidelineEntry entry) in notes)
        {
            if (!runsBySource.TryGetValue(source, out HashSet<string>? bookRuns))
            {
                string xhtml = File.ReadAllText(Path.Combine(bookDir, source));
                bookRuns = Runs(Words(WebUtility.HtmlDecode(Tag.Replace(xhtml, " ")))).ToHashSet(StringComparer.Ordinal);
                runsBySource.Add(source, bookRuns);
            }

            string? copied = Runs(Words(entry.Note)).FirstOrDefault(bookRuns.Contains);
            if (copied is not null)
            {
                errors.Add(new CheckError(
                    "meta.yaml",
                    CheckError.NoRule,
                    $"the {note}, section {entry.Section}, shares {CopiedRunLength} consecutive words with {source}: \"{copied}\"."));
            }
        }

        return errors;
    }

    /// <summary>The words of <paramref name="text"/>, lowercase, each without its punctuation; words of punctuation only are dropped.</summary>
    private static List<string> Words(string text)
    {
        var words = new List<string>();
        foreach (string token in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var word = new StringBuilder(token.Length);
            foreach (char c in token)
            {
                if (char.IsLetterOrDigit(c))
                {
                    word.Append(char.ToLowerInvariant(c));
                }
            }

            if (word.Length > 0)
            {
                words.Add(word.ToString());
            }
        }

        return words;
    }

    /// <summary>Every run of <see cref="CopiedRunLength"/> consecutive words, joined by single spaces.</summary>
    private static IEnumerable<string> Runs(List<string> words)
    {
        for (int i = 0; i + CopiedRunLength <= words.Count; i++)
        {
            yield return string.Join(' ', words.GetRange(i, CopiedRunLength));
        }
    }
}
