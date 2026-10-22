namespace Fdg.Cli.Tooling;

/// <summary>
/// Runs <c>dotnet build &lt;proj&gt; --no-restore -p:FdgSarifDir=&lt;dir&gt;</c> in the project directory. A forced
/// rebuild adds <c>--no-incremental --no-dependencies</c>, so only the project itself is recompiled.
/// </summary>
public sealed class DotnetBuildRunner : IBuildRunner
{
    private readonly IProcessRunner _runner;
    private readonly TimeSpan _timeout;

    public DotnetBuildRunner(IProcessRunner runner, TimeSpan timeout)
    {
        _runner = runner;
        _timeout = timeout;
    }

    public Task<ToolResult> BuildAsync(
        string projectPath,
        string sarifDirectory,
        bool noIncremental,
        CancellationToken cancellationToken)
    {
        List<string> arguments = ["build", projectPath, "--no-restore", "-p:FdgSarifDir=" + sarifDirectory];
        if (noIncremental)
        {
            arguments.Add("--no-incremental");
            arguments.Add("--no-dependencies");
        }

        return _runner.RunAsync("dotnet", arguments, Path.GetDirectoryName(projectPath)!, _timeout, cancellationToken);
    }
}
