using System.Globalization;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Layout;

/// <summary>
/// Reads the standard .editorconfig key <c>max_line_length</c>.
/// </summary>
internal static class LineLength
{
    public const int Default = 120;

    private const string Key = "max_line_length";

    /// <returns>
    /// The configured positive limit, or <see cref="Default"/> when the key is missing, <c>off</c>, or not a number.
    /// </returns>
    public static int Get(AnalyzerConfigOptions options)
    {
        if (options.TryGetValue(Key, out string? value)
            && int.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int length)
            && length > 0)
        {
            return length;
        }

        return Default;
    }
}
