namespace Fdg.Cli.Tooling;

/// <summary>
/// Runs <c>dotnet format &lt;proj&gt; --no-restore --severity warn</c> (dotnet format spells warning "warn") without a
/// subcommand, so whitespace, style and analyzers all run. The process starts in the project directory because
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
        IReadOnlyList<string> excludeDiagnostics,
        CancellationToken cancellationToken)
    {
        List<string> arguments = ["format", projectPath, "--no-restore", "--severity", "warn"];
        if (includeFiles is not null)
        {
            arguments.Add("--include");
            arguments.AddRange(includeFiles);
        }

        if (excludeDiagnostics.Count > 0)
        {
            arguments.Add("--exclude-diagnostics");
            arguments.AddRange(excludeDiagnostics);
        }

        string projectDirectory = Path.GetDirectoryName(projectPath)!;
        return _runner.RunAsync("dotnet", arguments, projectDirectory, _timeout, cancellationToken);
    }
}
