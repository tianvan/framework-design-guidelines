using System.Text;

namespace Fdg.Cli.Tooling;

/// <summary>
/// Runs <c>dotnet format &lt;proj&gt; --no-restore --severity warn --diagnostics ...</c> (dotnet format spells warning
/// "warn") without a subcommand, so whitespace, style and analyzers all run. <c>--diagnostics</c> limits the style and
/// analyzer fixes to those ids, so a project's other analyzers (<c>AnalysisMode=All</c>, CA1822) change nothing;
/// whitespace is formatted in full whatever the list holds (measured with SDK 10.0.402). An empty list runs
/// <c>dotnet format whitespace</c> only, since without <c>--diagnostics</c> every fix would apply. The process starts
/// in the project directory because
/// <c>--include</c> only matches paths relative to the working directory: absolute paths and paths starting with
/// <c>../</c> silently match nothing (measured with SDK 10.0.402).
/// </summary>
/// <remarks>
/// <para>
/// A command line longer than <see cref="MaxCommandLineLength"/> characters, which Windows would refuse at 32767,
/// passes the include list in a response file: <c>--include @&lt;file&gt;</c>. dotnet format reads one value per
/// line, whitespace and all, without quotes; a line starting with <c>#</c> is a comment and a value starting with
/// <c>@</c> is read as another response file (measured with SDK 10.0.403 and 10.0.114). Includes starting with
/// <c>@</c> or <c>#</c> therefore get a <c>./</c> prefix, which dotnet format still matches, here and on the
/// command line. The few names a line cannot hold, with a line break or leading or trailing whitespace, stay on the
/// command line after the response file.
/// </para>
/// </remarks>
public sealed class DotnetFormatRunner : IFormatRunner
{
    /// <summary>The longest command line passed as is; Windows allows 32767 characters including the program path.</summary>
    public const int MaxCommandLineLength = 30000;

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly IProcessRunner _runner;
    private readonly TimeSpan _timeout;

    public DotnetFormatRunner(IProcessRunner runner, TimeSpan timeout)
    {
        _runner = runner;
        _timeout = timeout;
    }

    public async Task<ToolResult> FormatAsync(
        string projectPath,
        IReadOnlyList<string>? includeFiles,
        IReadOnlyList<string> diagnostics,
        CancellationToken cancellationToken)
    {
        string[]? includes = includeFiles?.Select(EscapeInclude).ToArray();
        string projectDirectory = Path.GetDirectoryName(projectPath)!;
        List<string> arguments = Arguments(projectPath, includes, diagnostics);
        if (includes is null || CommandLineLength("dotnet", arguments) <= MaxCommandLineLength)
        {
            return await _runner.RunAsync("dotnet", arguments, projectDirectory, _timeout, cancellationToken)
                .ConfigureAwait(false);
        }

        string responseFile = Path.Combine(Path.GetTempPath(), "fdg-format-" + Guid.NewGuid().ToString("N") + ".rsp");
        try
        {
            string[] inFile = includes.Where(FitsOnALine).ToArray();
            await File.WriteAllLinesAsync(responseFile, inFile, Utf8NoBom, cancellationToken).ConfigureAwait(false);
            string[] onCommandLine = ["@" + responseFile, .. includes.Where(i => !FitsOnALine(i))];
            return await _runner
                .RunAsync("dotnet", Arguments(projectPath, onCommandLine, diagnostics), projectDirectory, _timeout, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            File.Delete(responseFile);
        }
    }

    /// <summary>
    /// The length of the command line the arguments make at most: each one with a separating space and the quotes
    /// it may need. Quotes inside an argument, which would add a backslash each, cannot occur in Windows paths.
    /// </summary>
    public static int CommandLineLength(string fileName, IEnumerable<string> arguments) =>
        fileName.Length + arguments.Sum(argument => argument.Length + 3);

    private static List<string> Arguments(string projectPath, IReadOnlyList<string>? includes, IReadOnlyList<string> diagnostics)
    {
        List<string> arguments = diagnostics.Count == 0
            ? ["format", "whitespace", projectPath, "--no-restore"]
            : ["format", projectPath, "--no-restore", "--severity", "warn"];
        if (includes is not null)
        {
            arguments.Add("--include");
            arguments.AddRange(includes);
        }

        if (diagnostics.Count > 0)
        {
            arguments.Add("--diagnostics");
            arguments.AddRange(diagnostics);
        }

        return arguments;
    }

    /// <summary>Keeps an include from being read as a response file, or as a comment inside one.</summary>
    private static string EscapeInclude(string include) =>
        include.StartsWith('@') || include.StartsWith('#') ? "./" + include : include;

    private static bool FitsOnALine(string include) =>
        include.Length > 0 &&
        include.IndexOfAny(['\r', '\n']) < 0 &&
        !char.IsWhiteSpace(include[0]) &&
        !char.IsWhiteSpace(include[^1]);
}
