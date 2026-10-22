namespace Fdg.Cli.Tooling;

/// <summary>Restores a project's packages.</summary>
public interface IRestoreRunner
{
    Task<ToolResult> RestoreAsync(string projectPath, CancellationToken cancellationToken);
}
