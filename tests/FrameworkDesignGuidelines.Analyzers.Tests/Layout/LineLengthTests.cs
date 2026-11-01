using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using FrameworkDesignGuidelines.Analyzers.Layout;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Layout;

public sealed class LineLengthTests
{
    [Theory]
    [InlineData(null, 120)]
    [InlineData("80", 80)]
    [InlineData(" 100 ", 100)]
    [InlineData("off", 120)]
    [InlineData("unset", 120)]
    [InlineData("0", 120)]
    [InlineData("-5", 120)]
    [InlineData("eighty", 120)]
    public void Get_reads_max_line_length(string? value, int expected)
    {
        Assert.Equal(expected, LineLength.Get(new Options(maxLineLength: value)));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    public void Tab_counts_as_tab_width_columns(int tabWidth)
    {
        SourceText text = SourceText.From("\tab\tc");
        TextLine line = text.Lines[0];
        int configured = LineLength.GetTabWidth(new Options(tabWidth: tabWidth.ToString(CultureInfo.InvariantCulture)));

        // A tab advances to the next tab stop, as editors count it: "\t" ends at tabWidth, "ab" adds two columns, and
        // the second tab moves on to 2 * tabWidth, not to tabWidth + 2 + tabWidth.
        Assert.Equal(tabWidth, configured);
        Assert.Equal((2 * tabWidth) + 1, LineLength.GetWidth(text, line.Span, configured));

        // Without tab_width, tab stops are four columns apart.
        Assert.Equal(9, LineLength.GetWidth(text, line.Span, LineLength.GetTabWidth(new Options())));
    }

    [Theory]
    [InlineData("2", 2)]
    [InlineData(" 8 ", 8)]
    [InlineData("0", 4)]
    [InlineData("-8", 4)]
    [InlineData("unset", 4)]
    [InlineData("tab", 4)]
    public void GetTabWidth_reads_a_positive_tab_width(string value, int expected)
    {
        Assert.Equal(expected, LineLength.GetTabWidth(new Options(tabWidth: value)));
    }

    private sealed class Options(string? maxLineLength = null, string? tabWidth = null) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
        {
            value = key switch
            {
                "max_line_length" => maxLineLength,
                "tab_width" => tabWidth,
                _ => null,
            };
            return value is not null;
        }
    }
}
