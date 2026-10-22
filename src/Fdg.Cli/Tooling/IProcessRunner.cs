namespace Fdg.Cli.Tooling;

/// <summary>Starts external processes; replaced by a fake in tests.</summary>
public interface IProcessRunner
{
    /// <param name="timeout">How long to wait before killing the process tree; <see cref="Timeout.InfiniteTimeSpan"/> to wait forever.</param>
    Task<ToolResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}
