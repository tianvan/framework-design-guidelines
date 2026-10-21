using System.Diagnostics.CodeAnalysis;
using FrameworkDesignGuidelines.Analyzers.Layout;
using Microsoft.CodeAnalysis.Diagnostics;

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
        Assert.Equal(expected, LineLength.Get(new Options(value)));
    }

    private sealed class Options(string? maxLineLength) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
        {
            value = key == "max_line_length" ? maxLineLength : null;
            return value is not null;
        }
    }
}
