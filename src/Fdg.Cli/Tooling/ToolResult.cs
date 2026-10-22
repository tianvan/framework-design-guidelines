namespace Fdg.Cli.Tooling;

/// <summary>Outcome of an external tool run: exit code, interleaved stdout and stderr, and whether it timed out.</summary>
public sealed record ToolResult(int ExitCode, string Output, bool TimedOut)
{
    public bool Succeeded => ExitCode == 0 && !TimedOut;

    /// <summary>The first <paramref name="count"/> non-empty lines of the output.</summary>
    public IReadOnlyList<string> FirstLines(int count) =>
        Output.ReplaceLineEndings("\n")
            .Split('\n')
            .Where(l => l.Trim().Length > 0)
            .Take(count)
            .ToArray();
}
