using Microsoft.CodeAnalysis.Text;

namespace FrameworkDesignGuidelines.Analyzers.Layout;

/// <summary>
/// Line and indentation helpers shared by the layout analyzers and their code fixes.
/// </summary>
internal static class LayoutText
{
    /// <summary>One indentation level, as the book's samples and the Roslyn defaults use.</summary>
    public const string IndentUnit = "    ";

    private const int TabWidth = 4;

    /// <summary>Returns the leading spaces and tabs of the line that contains <paramref name="position"/>.</summary>
    public static string GetLineIndentation(SourceText text, int position)
    {
        TextLine line = text.Lines.GetLineFromPosition(position);
        int end = line.Start;
        while (end < line.End && text[end] is ' ' or '\t')
        {
            end++;
        }

        return text.ToString(TextSpan.FromBounds(line.Start, end));
    }

    /// <summary>Returns the visual width of an indentation string, counting a tab as four columns.</summary>
    public static int GetWidth(string indentation)
    {
        int width = 0;
        foreach (char c in indentation)
        {
            width = c == '\t' ? width + TabWidth - (width % TabWidth) : width + 1;
        }

        return width;
    }

    /// <summary>
    /// Returns one indentation level in the style of <paramref name="lineIndentation"/>: a tab when that line is
    /// indented with tabs, otherwise <see cref="IndentUnit"/>.
    /// </summary>
    public static string GetIndentUnit(string lineIndentation)
    {
        return lineIndentation.IndexOf('\t') >= 0 ? "\t" : IndentUnit;
    }

    /// <summary>Builds indentation of the given visual width, with tabs (then spaces) or with spaces only.</summary>
    public static string CreateIndentation(int width, bool useTabs)
    {
        return useTabs
            ? new string('\t', width / TabWidth) + new string(' ', width % TabWidth)
            : new string(' ', width);
    }

    public static int GetLineNumber(SourceText text, int position)
    {
        return text.Lines.GetLineFromPosition(position).LineNumber;
    }
}
