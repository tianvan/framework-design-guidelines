using System.Globalization;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace FrameworkDesignGuidelines.Analyzers.Layout;

/// <summary>
/// Reads the standard .editorconfig keys <c>max_line_length</c> and <c>tab_width</c>, and measures text in the
/// columns that <c>max_line_length</c> counts.
/// </summary>
internal static class LineLength
{
    public const int Default = 120;

    public const int DefaultTabWidth = 4;

    private const string Key = "max_line_length";

    private const string TabWidthKey = "tab_width";

    /// <returns>
    /// The configured positive limit, or <see cref="Default"/> when the key is missing, <c>off</c>, or not a number.
    /// </returns>
    public static int Get(AnalyzerConfigOptions options)
    {
        return GetPositive(options, Key) ?? Default;
    }

    /// <returns>
    /// The configured positive <c>tab_width</c>, or <see cref="DefaultTabWidth"/> when the key is missing or not a
    /// positive number.
    /// </returns>
    public static int GetTabWidth(AnalyzerConfigOptions options)
    {
        return GetPositive(options, TabWidthKey) ?? DefaultTabWidth;
    }

    /// <summary>
    /// Returns the column reached after <paramref name="span"/> when it starts at the start of a line.
    /// </summary>
    public static int GetWidth(SourceText text, TextSpan span, int tabWidth)
    {
        return Advance(text, span, 0, tabWidth);
    }

    /// <summary>
    /// Returns the column reached after <paramref name="span"/> when it starts at <paramref name="column"/>. A tab
    /// advances to the next tab stop, a multiple of <paramref name="tabWidth"/>, as editors and the indentation
    /// widths in <see cref="LayoutText"/> count it; any other character takes one column.
    /// </summary>
    public static int Advance(SourceText text, TextSpan span, int column, int tabWidth)
    {
        for (int position = span.Start; position < span.End; position++)
        {
            column = text[position] == '\t' ? column + tabWidth - (column % tabWidth) : column + 1;
        }

        return column;
    }

    private static int? GetPositive(AnalyzerConfigOptions options, string key)
    {
        if (options.TryGetValue(key, out string? value)
            && int.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int number)
            && number > 0)
        {
            return number;
        }

        return null;
    }
}
