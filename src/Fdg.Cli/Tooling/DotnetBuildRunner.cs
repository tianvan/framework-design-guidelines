namespace Fdg.Cli.Tooling;

/// <summary>
/// Runs <c>dotnet build &lt;proj&gt; --no-restore -p:FdgSarifDir=&lt;dir&gt; -p:FdgSarifProject=&lt;proj&gt;</c> in the
/// project directory. Global properties flow to referenced projects, so <c>FdgSarifProject</c> tells the package
/// which project writes the log. <c>-p:CopyRetryCount=0</c>: while the app is running, its files in <c>bin/</c> are
/// locked, and the default 10 retries of the copy cost about 10 seconds per Stop for the same failure (measured with
/// SDK 10.0.402). A forced rebuild adds <c>--no-incremental --no-dependencies</c>, so only the project itself is
/// recompiled.
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
            arguments.Add("--no-dependencies");
        }

        return _runner.RunAsync("dotnet", arguments, Path.GetDirectoryName(projectPath)!, _timeout, cancellationToken);
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
