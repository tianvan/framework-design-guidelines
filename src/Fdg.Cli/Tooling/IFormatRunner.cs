namespace Fdg.Cli.Tooling;

/// <summary>Formats a project: all whitespace, then the code style and analyzer fixes of the given diagnostics.</summary>
public interface IFormatRunner
{
    /// <param name="projectPath">Full path of the .csproj.</param>
    /// <param name="includeFiles">
    /// <see langword="null"/> for the whole project; otherwise files and directories in the form
    /// <see cref="FormatIncludes.ToProjectRelative"/> produces: relative to the project directory, forward slashes,
    /// directories ending in <c>/</c>.
    /// </param>
    /// <param name="diagnostics">
    /// The diagnostic ids whose fixes may be applied; empty for none, so only whitespace is formatted. Whitespace is
    /// formatted in full either way.
    /// </param>
    Task<ToolResult> FormatAsync(
        string projectPath,
        IReadOnlyList<string>? includeFiles,
        IReadOnlyList<string> diagnostics,
        CancellationToken cancellationToken);
}
