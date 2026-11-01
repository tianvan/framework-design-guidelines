namespace Fdg.Cli.Tooling;

/// <summary>
/// Runs <c>dotnet format &lt;proj&gt; --no-restore --severity warn --diagnostics ...</c> (dotnet format spells warning
/// "warn") without a subcommand, so whitespace, style and analyzers all run. <c>--diagnostics</c> limits the style and
/// analyzer fixes to those ids, so a project's other analyzers (<c>AnalysisMode=All</c>, CA1822) change nothing;
/// whitespace is formatted in full whatever the list holds (measured with SDK 10.0.402). An empty list runs
/// <c>dotnet format whitespace</c> only, since without <c>--diagnostics</c> every fix would apply. The process starts
/// in the project directory because
/// <c>--include</c> only matches paths relative to the working directory: absolute paths and paths starting with
/// <c>../</c> silently match nothing (measured with SDK 10.0.402). <c>--exclude</c> takes the same relative paths, but
/// a <c>../</c> path does match there, so a file linked from outside the project directory can be left out; an
/// excluded file is left out of all three formatters (measured with SDK 10.0.403 and 10.0.114).
/// </summary>
/// <remarks>
/// <para>
/// Windows refuses a command line over 32767 characters, so a long include list is split into batches, one run
/// each, whose command lines stay within <see cref="MaxCommandLineLength"/> characters. A response file does not
/// help: <c>dotnet format</c> is forwarded, so the dotnet CLI expands <c>@file</c> itself and starts
/// <c>dotnet exec &lt;sdk&gt;/DotnetTools/dotnet-format/dotnet-format.dll</c> with every argument, which fails with
/// "The filename or extension is too long" (measured with SDK 10.0.403 and 10.0.114). That forwarded command line is
/// longer by the dll path, so <see cref="ForwarderAllowance"/> characters are kept free for it.
/// </para>
/// <para>
/// Batches run one after another; the first failed run ends the call. The excludes go to every batch. For the same
/// reason as response files, an include or exclude starting with <c>@</c> gets a <c>./</c> prefix, which dotnet
/// format still matches.
/// </para>
/// </remarks>
public sealed class DotnetFormatRunner : IFormatRunner
{
    /// <summary>The longest command line a run may have, forwarder included; Windows allows 32767 characters.</summary>
    public const int MaxCommandLineLength = 30000;

    /// <summary>
    /// Room left for what the dotnet CLI adds when it forwards to dotnet-format: <c>exec</c> and the dll path.
    /// </summary>
    public const int ForwarderAllowance = 1000;

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
        IReadOnlyList<string> excludeFiles,
        IReadOnlyList<string> diagnostics,
        CancellationToken cancellationToken)
    {
        string projectDirectory = Path.GetDirectoryName(projectPath)!;
        string[] excludes = excludeFiles.Select(EscapePath).ToArray();
        if (includeFiles is null)
        {
            return await _runner
                .RunAsync("dotnet", Arguments(projectPath, null, excludes, diagnostics), projectDirectory, _timeout, cancellationToken)
                .ConfigureAwait(false);
        }

        int room = MaxCommandLineLength - ForwarderAllowance - CommandLineLength("dotnet", Arguments(projectPath, [], excludes, diagnostics));
        ToolResult result = new(0, string.Empty, false);
        foreach (IReadOnlyList<string> batch in Batches(includeFiles.Select(EscapePath).ToArray(), room))
        {
            result = await _runner
                .RunAsync("dotnet", Arguments(projectPath, batch, excludes, diagnostics), projectDirectory, _timeout, cancellationToken)
                .ConfigureAwait(false);
            if (!result.Succeeded)
            {
                break;
            }
        }

        return result;
    }

    /// <summary>
    /// The length of the command line the arguments make at most: each one with a separating space and the quotes
    /// it may need. Quotes inside an argument, which would add a backslash each, cannot occur in Windows paths.
    /// </summary>
    public static int CommandLineLength(string fileName, IEnumerable<string> arguments) =>
        fileName.Length + ArgumentsLength(arguments);

    /// <summary>What the arguments add to a command line at most; see <see cref="CommandLineLength"/>.</summary>
    public static int ArgumentsLength(IEnumerable<string> arguments) => arguments.Sum(argument => argument.Length + 3);

    /// <summary>
    /// Splits the includes, in order, into lists of at most <paramref name="room"/> characters each. An empty list
    /// is one empty batch; a single include longer than the room still gets a batch of its own.
    /// </summary>
    private static IEnumerable<IReadOnlyList<string>> Batches(string[] includes, int room)
    {
        var batch = new List<string>();
        int length = 0;
        foreach (string include in includes)
        {
            int cost = ArgumentsLength([include]);
            if (batch.Count > 0 && length + cost > room)
            {
                yield return batch;
                batch = [];
                length = 0;
            }

            batch.Add(include);
            length += cost;
        }

        yield return batch;
    }

    private static List<string> Arguments(
        string projectPath,
        IReadOnlyList<string>? includes,
        IReadOnlyList<string> excludes,
        IReadOnlyList<string> diagnostics)
    {
        List<string> arguments = diagnostics.Count == 0
            ? ["format", "whitespace", projectPath, "--no-restore"]
            : ["format", projectPath, "--no-restore", "--severity", "warn"];
        if (includes is not null)
        {
            arguments.Add("--include");
            arguments.AddRange(includes);
        }

        if (excludes.Count > 0)
        {
            arguments.Add("--exclude");
            arguments.AddRange(excludes);
        }

        if (diagnostics.Count > 0)
        {
            arguments.Add("--diagnostics");
            arguments.AddRange(diagnostics);
        }

        return arguments;
    }

    /// <summary>Keeps a path from being read as a response file by the dotnet CLI.</summary>
    private static string EscapePath(string path) => path.StartsWith('@') ? "./" + path : path;
}
