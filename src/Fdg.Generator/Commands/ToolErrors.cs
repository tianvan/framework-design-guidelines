using System.ComponentModel;
using System.Text.Json;
using Fdg.Generator.Book;
using YamlDotNet.Core;

namespace Fdg.Generator.Commands;

/// <summary>
/// The error output of the maintainer commands: failures that a maintainer can fix come out as one line instead of a
/// stack trace.
/// </summary>
public static class ToolErrors
{
    /// <summary>
    /// Runs a maintainer command and turns expected failures into one line on <paramref name="error"/>, exit code 2.
    /// </summary>
    /// <remarks>
    /// The expected failures are malformed YAML, JSON and book files, files that cannot be read, and a missing or
    /// failing <c>dotnet</c>. The line reads <c>&lt;command&gt; failed: &lt;message&gt;</c>, with the line breaks
    /// of the message replaced by spaces. Any other exception is a bug of the tool and keeps its stack trace.
    /// </remarks>
    public static int Run(string command, TextWriter error, Func<int> body)
    {
        try
        {
            return body();
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            error.WriteLine($"{command} failed: {exception.Message.ReplaceLineEndings(" ").Trim()}");
            return 2;
        }
    }

    private static bool IsExpected(Exception exception)
    {
        return exception is YamlException or FormatException or JsonException or BookFormatException
            or IOException or UnauthorizedAccessException
            or Win32Exception
            or InvalidOperationException;
    }
}
