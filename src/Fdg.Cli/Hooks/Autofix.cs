using System.Globalization;

namespace Fdg.Cli.Hooks;

/// <summary>A file the Stop hook changed, with the changed line ranges of the formatted version.</summary>
/// <param name="Path">Relative to the repository root, with forward slashes.</param>
public sealed record AutofixFile(string Path, IReadOnlyList<LineRange> Ranges);

/// <summary>What the last Stop hook of a turn changed, told to the AI when the next turn starts.</summary>
public sealed record AutofixRecord(int Turn, IReadOnlyList<AutofixFile> Files)
{
    /// <summary>
    /// One line, for example <c>FDG auto-formatted at the end of the last turn: src/A.cs (lines 12-30, 41-44);
    /// src/B.cs (line 3). Re-read those regions before editing them.</c>
    /// </summary>
    public string Summary()
    {
        IEnumerable<string> files = Files.Select(f =>
        {
            string ranges = string.Join(", ", f.Ranges.Select(r => r.Start == r.End
                ? r.Start.ToString(CultureInfo.InvariantCulture)
                : $"{r.Start}-{r.End}"));
            bool single = f.Ranges.Count == 1 && f.Ranges[0].Start == f.Ranges[0].End;
            return $"{f.Path} ({(single ? "line" : "lines")} {ranges})";
        });
        return "FDG auto-formatted at the end of the last turn: " + string.Join("; ", files) +
            ". Re-read those regions before editing them.";
    }
}
