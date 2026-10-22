using System.Globalization;

namespace Fdg.Cli.Hooks;

/// <summary>A file the Stop hook changed, with the changed line ranges of the formatted version.</summary>
/// <param name="Path">Relative to the repository root, with forward slashes.</param>
public sealed record AutofixFile(string Path, IReadOnlyList<LineRange> Ranges);

/// <summary>What the last Stop hook of a turn changed, told to the AI when the next turn starts.</summary>
public sealed record AutofixRecord(int Turn, IReadOnlyList<AutofixFile> Files)
{
    /// <summary>One line, for example <c>... re-read them before editing: src/A.cs (lines 3-5, 8).</c></summary>
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
        return "FDG reformatted files after your last turn; re-read them before editing: " + string.Join("; ", files) + ".";
    }
}
