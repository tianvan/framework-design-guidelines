namespace Fdg.Cli.Tooling;

/// <summary>Builds a project with its SARIF log written to a given directory.</summary>
public interface IBuildRunner
{
    /// <param name="projectPath">Full path of the .csproj.</param>
    /// <param name="sarifDirectory">Passed as <c>FdgSarifDir</c>; the package writes one <c>&lt;tfm&gt;.sarif</c> per framework there.</param>
    /// <param name="noIncremental">
    /// Recompile even when MSBuild considers the project up to date; referenced projects are not built.
    /// </param>
    /// <param name="noDependencies">Build the project only, against the existing output of the projects it references.</param>
    Task<ToolResult> BuildAsync(
        string projectPath,
        string sarifDirectory,
        bool noIncremental,
        bool noDependencies,
        CancellationToken cancellationToken);
}
