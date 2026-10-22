namespace Fdg.Cli.Hooks;

/// <summary>
/// Decides which diagnostics the Stop hook reports: compiler errors anywhere in a changed file, and warnings or
/// errors of registered diagnostics on changed lines. Suggestions, suppressed results, unregistered warnings
/// (other analyzers, nullable warnings) and files the AI did not change are ignored.
/// </summary>
public static class DiagnosticFilter
{
    /// <param name="changedLines">Changed lines per full file path, for the files changed in this turn.</param>
    public static bool IsReported(
        SarifDiagnostic diagnostic,
        IReadOnlyDictionary<string, IReadOnlySet<int>> changedLines,
        IReadOnlySet<string> registeredDiagnostics)
    {
        if (diagnostic.Suppressed || !changedLines.TryGetValue(diagnostic.FilePath, out IReadOnlySet<int>? lines))
        {
            return false;
        }

        if (IsCompileError(diagnostic))
        {
            return true;
        }

        return (diagnostic.Level == "error" || diagnostic.Level == "warning") &&
            registeredDiagnostics.Contains(diagnostic.RuleId) &&
            lines.Contains(diagnostic.Line);
    }

    /// <summary>A compiler error (<c>CS</c>) not suppressed in source.</summary>
    public static bool IsCompileError(SarifDiagnostic diagnostic) =>
        !diagnostic.Suppressed &&
        diagnostic.Level == "error" &&
        diagnostic.RuleId.StartsWith("CS", StringComparison.Ordinal);
}
