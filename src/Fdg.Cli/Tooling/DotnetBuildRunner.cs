namespace Fdg.Cli.Tooling;

/// <summary>
/// Runs <c>dotnet build &lt;proj&gt; --no-restore -p:FdgSarifDir=&lt;dir&gt; -p:FdgSarifProject=&lt;proj&gt;</c> in the
/// project directory. Global properties flow to referenced projects, so <c>FdgSarifProject</c> tells the package
/// which project writes the log. <c>-p:CopyRetryCount=0</c>: while the app is running, its files in <c>bin/</c> are
/// locked, and the default 10 retries of the copy cost about 10 seconds per Stop for the same failure (measured with
/// SDK 10.0.402). A forced rebuild adds <c>--no-incremental --no-dependencies</c>, so only the project itself is
/// recompiled; a build without dependencies adds <c>--no-dependencies</c> only, so referenced projects are neither
/// checked nor built and their existing output is used.
/// </summary>
/// <remarks>
/// MSBuild splits a <c>-p:</c> value on <c>;</c> and <c>,</c> and unescapes <c>%XX</c>, so both paths are escaped
/// (<see cref="EscapeProperty"/>); a repository path such as <c>C:\src\a;b,c</c> then reaches the package intact. The
/// project argument itself is a plain path.
/// </remarks>
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
        bool noDependencies,
        CancellationToken cancellationToken)
    {
        List<string> arguments =
        [
            "build",
            projectPath,
            "--no-restore",
            "-p:FdgSarifDir=" + EscapeProperty(sarifDirectory),
            "-p:FdgSarifProject=" + EscapeProperty(projectPath),
            "-p:CopyRetryCount=0",
        ];
        if (noIncremental)
        {
            arguments.Add("--no-incremental");
        }

        if (noIncremental || noDependencies)
        {
            arguments.Add("--no-dependencies");
        }

        return _runner.RunAsync("dotnet", arguments, Path.GetDirectoryName(projectPath)!, _timeout, cancellationToken);
    }

    /// <summary>
    /// Whether the package can have the compiler write a SARIF log to <paramref name="sarifDirectory"/>. The compiler
    /// splits <c>/errorlog:</c> on <c>,</c>, so the package passes the log's path relative to the project directory when
    /// the full path has a comma, and requests no log when that relative path still has one: a git directory or
    /// worktree elsewhere whose path has a comma.
    /// </summary>
    public static bool CanWriteLog(string projectPath, string sarifDirectory)
    {
        string log = Path.Combine(sarifDirectory, "single.sarif");
        return !log.Contains(',') || !Path.GetRelativePath(Path.GetDirectoryName(projectPath)!, log).Contains(',');
    }

    /// <summary>
    /// Escapes a value for <c>-p:Name=value</c>: the characters MSBuild treats specially (<c>% $ @ ' ; ? *</c>, as
    /// <c>[MSBuild]::Escape</c> does) and the <c>,</c> that separates properties on the command line become
    /// <c>%XX</c>, which MSBuild turns back into the character when it uses the value.
    /// </summary>
    public static string EscapeProperty(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (char c in value)
        {
            if (c is '%' or '$' or '@' or '\'' or ';' or '?' or '*' or ',')
            {
                builder.Append('%').Append(((int)c).ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}
