using System.Text.RegularExpressions;

namespace Fdg.Generator.Book;

/// <summary>Text clean-up shared by every kind of block.</summary>
public static partial class Normalizer
{
    // The book prints the digit zero as this glyph inside code.
    private const char ZeroGlyph = 'ʘ';

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    // Split ligatures found by scanning the whole book (2026-10-23); a heuristic would join real words such as "off the".
    private static readonly (string Split, string Joined)[] SplitLigatures = [("Conf licts", "Conflicts")];

    public static string Glyphs(string text)
    {
        return text.Replace(ZeroGlyph, '0');
    }

    /// <summary>Rejoins the words that the epub breaks at a ligature; only the known splits are touched.</summary>
    public static string Ligatures(string text)
    {
        foreach ((string split, string joined) in SplitLigatures)
        {
            text = text.Replace(split, joined, StringComparison.Ordinal);
        }

        return text;
    }

    /// <summary>Collapses every run of whitespace to one space and trims the ends.</summary>
    public static string Collapse(string text)
    {
        return Ligatures(Whitespace().Replace(Glyphs(text), " ").Trim());
    }

    /// <summary>Normalizes line endings, drops trailing whitespace, and trims blank lines at both ends.</summary>
    public static string Code(string text)
    {
        string unified = Ligatures(Glyphs(text)).Replace("\r\n", "\n").Replace('\r', '\n');
        string[] lines = unified.Split('\n').Select(line => line.TrimEnd()).ToArray();

        int start = 0;
        while (start < lines.Length && lines[start].Length == 0)
        {
            start++;
        }

        int end = lines.Length;
        while (end > start && lines[end - 1].Length == 0)
        {
            end--;
        }

        return string.Join('\n', lines[start..end]);
    }
}
