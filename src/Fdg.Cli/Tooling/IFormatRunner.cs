namespace Fdg.Cli.Tooling;

/// <summary>Formats a project: whitespace, code style and analyzer fixes, in that order.</summary>
public interface IFormatRunner
{
    /// <param name="projectPath">Full path of the .csproj.</param>
    /// <param name="includeFiles">
    /// <see langword="null"/> for the whole project; otherwise files and directories in the form
    /// <see cref="FormatIncludes.ToProjectRelative"/> produces: relative to the project directory, forward slashes,
    /// directories ending in <c>/</c>.
    /// </param>
    /// <param name="excludeDiagnostics">Diagnostic ids whose fixes must not be applied.</param>
    Task<ToolResult> FormatAsync(
        string projectPath,
        IReadOnlyList<string>? includeFiles,
        IReadOnlyList<string> excludeDiagnostics,
        CancellationToken cancellationToken);
}
