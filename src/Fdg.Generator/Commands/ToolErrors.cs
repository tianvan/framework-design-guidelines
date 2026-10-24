using System.ComponentModel;
using System.Text.Json;
using Fdg.Generator.Book;
using Json.Schema;
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
    /// The expected failures are malformed YAML, JSON and book files, a schema that is invalid or has a reference that
    /// cannot be resolved, files that cannot be read, and a missing or failing <c>dotnet</c>. The line reads
    /// <c>&lt;command&gt; failed: &lt;message&gt;</c>, with the line breaks of the message replaced by spaces. Any
    /// other exception is a bug of the tool and keeps its stack trace, including an
    /// <see cref="ObjectDisposedException"/>, although it is an <see cref="InvalidOperationException"/> too.
    /// </remarks>
    public static int Run(string command, TextWriter error, Func<int> body)
    {
        try
        {
            return body();
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            error.WriteLine($"{command} failed: {OneLine(exception.Message)}");
            return 2;
        }
    }

    /// <summary>
    /// The message on one line, each line break replaced by a space. The message of a failing <c>dotnet</c> and the
    /// list of SDK analyzers that could not be loaded span several lines.
    /// </summary>
    internal static string OneLine(string message)
    {
        return message.ReplaceLineEndings(" ").Trim();
    }

    private static bool IsExpected(Exception exception)
    {
        return exception is YamlException or FormatException or JsonException or JsonSchemaException
            or BookFormatException or IOException or UnauthorizedAccessException
            or Win32Exception
            or (InvalidOperationException and not ObjectDisposedException);
    }
}
