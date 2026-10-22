namespace Fdg.Cli.Tooling;

/// <summary>Runs <c>dotnet restore &lt;proj&gt;</c> in the project directory, so its global.json applies.</summary>
public sealed class DotnetRestoreRunner : IRestoreRunner
{
    private readonly IProcessRunner _runner;
    private readonly TimeSpan _timeout;

    public DotnetRestoreRunner(IProcessRunner runner, TimeSpan timeout)
    {
        _runner = runner;
        _timeout = timeout;
    }

    public Task<ToolResult> RestoreAsync(string projectPath, CancellationToken cancellationToken) =>
        _runner.RunAsync(
            "dotnet",
            ["restore", projectPath],
            Path.GetDirectoryName(projectPath)!,
            _timeout,
            cancellationToken);
}
