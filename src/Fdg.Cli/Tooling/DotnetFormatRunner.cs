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
public sealed class DotnetFormatRunner : IFormatRunner
{
    private readonly IProcessRunner _runner;
    private readonly TimeSpan _timeout;

    public DotnetFormatRunner(IProcessRunner runner, TimeSpan timeout)
    {
        _runner = runner;
        _timeout = timeout;
    }

    public Task<ToolResult> FormatAsync(
        string projectPath,
        IReadOnlyList<string>? includeFiles,
        IReadOnlyList<string> diagnostics,
        CancellationToken cancellationToken)
    {
        List<string> arguments = diagnostics.Count == 0
            ? ["format", "whitespace", projectPath, "--no-restore"]
            : ["format", projectPath, "--no-restore", "--severity", "warn"];
        if (includeFiles is not null)
        {
            arguments.Add("--include");
            arguments.AddRange(includeFiles);
        }

        if (diagnostics.Count > 0)
        {
            arguments.Add("--diagnostics");
            arguments.AddRange(diagnostics);
        }

        string projectDirectory = Path.GetDirectoryName(projectPath)!;
        return _runner.RunAsync("dotnet", arguments, projectDirectory, _timeout, cancellationToken);
    }
}
