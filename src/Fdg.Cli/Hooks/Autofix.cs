using System.Globalization;
using Fdg.Cli.Projects;

namespace Fdg.Cli.Hooks;

/// <summary>A file the Stop hook changed, with the changed line ranges of the formatted version.</summary>
/// <param name="Path">Relative to the repository root, with forward slashes.</param>
/// <param name="Ranges">The changed lines of the formatted version, in order.</param>
/// <param name="LineCount">
/// How many lines the file has after the fix; 0 when unknown, as in a record written by an earlier version.
/// </param>
public sealed record AutofixFile(string Path, IReadOnlyList<LineRange> Ranges, int LineCount = 0);

/// <summary>What the Stop hooks of a turn changed, told to the AI when the next turn starts.</summary>
public sealed record AutofixRecord(int Turn, IReadOnlyList<AutofixFile> Files)
{
    /// <summary>A file with more ranges than this is summarized as reformatted throughout.</summary>
    public const int MaxListedRanges = 10;

    /// <summary>
    /// The record after another Stop in the same turn: the files of both, by path, with their ranges joined and the
    /// overlapping or adjacent ones made one. A record of another turn is replaced. Ranges from an earlier Stop are
    /// not shifted by later edits, so they can be slightly off.
    /// </summary>
    public static AutofixRecord Merge(AutofixRecord? previous, AutofixRecord current)
    {
        if (previous is null || previous.Turn != current.Turn)
        {
            return current;
        }

        var merged = new Dictionary<string, AutofixFile>(ProjectFiles.PathComparer);
        foreach (AutofixFile file in previous.Files.Concat(current.Files))
        {
            merged[file.Path] = merged.TryGetValue(file.Path, out AutofixFile? earlier)
                ? new AutofixFile(
                    earlier.Path,
                    Coalesce(earlier.Ranges.Concat(file.Ranges)),
                    file.LineCount > 0 ? file.LineCount : earlier.LineCount)
                : file with { Ranges = Coalesce(file.Ranges) };
        }

        return current with { Files = merged.Values.OrderBy(f => f.Path, StringComparer.Ordinal).ToArray() };
    }

    /// <summary>
    /// One line, for example <c>FDG auto-formatted at the end of the last turn: src/A.cs (lines 12-30, 41-44);
    /// src/B.cs (line 3); src/C.cs (reformatted throughout). Re-read those regions before editing them.</c>
    /// </summary>
    public string Summary()
    {
        IEnumerable<string> files = Files.Select(f =>
        {
            IReadOnlyList<LineRange> ranges = Coalesce(f.Ranges);
            int covered = ranges.Sum(r => r.End - r.Start + 1);
            if (ranges.Count > MaxListedRanges || (f.LineCount > 0 && covered * 2 > f.LineCount))
            {
                return $"{f.Path} (reformatted throughout)";
            }

            string listed = string.Join(", ", ranges.Select(r => r.Start == r.End
                ? r.Start.ToString(CultureInfo.InvariantCulture)
                : $"{r.Start}-{r.End}"));
            bool single = ranges.Count == 1 && ranges[0].Start == ranges[0].End;
            return $"{f.Path} ({(single ? "line" : "lines")} {listed})";
        });
        return "FDG auto-formatted at the end of the last turn: " + string.Join("; ", files) +
            ". Re-read those regions before editing them.";
    }

    private static List<LineRange> Coalesce(IEnumerable<LineRange> ranges)
    {
        var result = new List<LineRange>();
        foreach (LineRange range in ranges.OrderBy(r => r.Start).ThenBy(r => r.End))
        {
            if (result.Count > 0 && range.Start <= result[^1].End + 1)
            {
                result[^1] = result[^1] with { End = Math.Max(result[^1].End, range.End) };
            }
            else
            {
                result.Add(range);
            }
        }

        return result;
    }
}
