using System.Globalization;

namespace Fdg.Cli.Hooks;

/// <summary>Lines <see cref="Start"/> through <see cref="End"/>, 1-based and inclusive.</summary>
public sealed record LineRange(int Start, int End);

/// <summary>
/// One <c>@@ -a,b +c,d @@</c> header: <paramref name="OldCount"/> lines from <paramref name="OldStart"/> became
/// <paramref name="NewCount"/> lines from <paramref name="NewStart"/>. A count of 0 puts the start on the line before.
/// </summary>
public sealed record DiffHunk(int OldStart, int OldCount, int NewStart, int NewCount);

/// <summary>The lines of a file that differ from another version, from <c>git diff --no-index -U0</c>.</summary>
public static class ChangedLines
{
    /// <summary>
    /// Lines of <paramref name="currentPath"/> changed relative to <paramref name="baselinePath"/>, ignoring
    /// whitespace-only and line-ending changes. Pure deletions change no line.
    /// </summary>
    public static async Task<IReadOnlySet<int>> ComputeAsync(
        GitRepository git,
        string baselinePath,
        string currentPath,
        CancellationToken cancellationToken)
    {
        string diff = await git.DiffFilesAsync(baselinePath, currentPath, ignoreWhitespace: true, cancellationToken)
            .ConfigureAwait(false);
        return Lines(ParseHunks(diff));
    }

    /// <summary>
    /// Lines of the formatted file the AI changed this turn: the lines of <paramref name="aiPath"/> changed relative
    /// to <paramref name="baselinePath"/>, carried through the formatter's changes to <paramref name="formattedPath"/>.
    /// A formatter change counts only where it touches a line the AI changed, so old code the formatter rewrote
    /// (braces added, a block expanded, usings sorted) is not the AI's change.
    /// </summary>
    public static async Task<IReadOnlySet<int>> ComputeAsync(
        GitRepository git,
        string baselinePath,
        string aiPath,
        string formattedPath,
        CancellationToken cancellationToken)
    {
        IReadOnlySet<int> changed = await ComputeAsync(git, baselinePath, aiPath, cancellationToken).ConfigureAwait(false);
        string formatting = await git.DiffFilesAsync(aiPath, formattedPath, ignoreWhitespace: true, cancellationToken)
            .ConfigureAwait(false);
        return MapThrough(changed, ParseHunkHeaders(formatting));
    }

    /// <summary>
    /// Maps lines of a file to the file <paramref name="hunks"/> turn it into: a line outside every hunk moves by
    /// the lines the hunks before it added or removed; a hunk that replaces any of the lines, or inserts next to
    /// one, contributes all of its new lines.
    /// </summary>
    public static IReadOnlySet<int> MapThrough(IReadOnlySet<int> lines, IReadOnlyList<DiffHunk> hunks)
    {
        var mapped = new HashSet<int>();
        foreach (int line in lines)
        {
            int offset = 0;
            bool replaced = false;
            foreach (DiffHunk hunk in hunks)
            {
                if (hunk.OldCount > 0 && line >= hunk.OldStart && line < hunk.OldStart + hunk.OldCount)
                {
                    replaced = true;
                    break;
                }

                bool before = hunk.OldCount > 0 ? hunk.OldStart + hunk.OldCount <= line : hunk.OldStart < line;
                if (!before)
                {
                    break;
                }

                offset += hunk.NewCount - hunk.OldCount;
            }

            if (!replaced)
            {
                mapped.Add(line + offset);
            }
        }

        foreach (DiffHunk hunk in hunks.Where(h => h.NewCount > 0))
        {
            bool touches = hunk.OldCount > 0
                ? Enumerable.Range(hunk.OldStart, hunk.OldCount).Any(lines.Contains)
                : lines.Contains(hunk.OldStart) || lines.Contains(hunk.OldStart + 1);
            if (touches)
            {
                mapped.UnionWith(Enumerable.Range(hunk.NewStart, hunk.NewCount));
            }
        }

        return mapped;
    }

    /// <summary>The new-side ranges of the <c>@@ -a,b +c,d @@</c> headers; pure deletions have none.</summary>
    public static IReadOnlyList<LineRange> ParseHunks(string diff) =>
        ParseHunkHeaders(diff)
            .Where(h => h.NewCount > 0)
            .Select(h => new LineRange(h.NewStart, h.NewStart + h.NewCount - 1))
            .ToArray();

    /// <summary>The <c>@@ -a,b +c,d @@</c> headers in order; a missing count means 1.</summary>
    public static IReadOnlyList<DiffHunk> ParseHunkHeaders(string diff)
    {
        var hunks = new List<DiffHunk>();
        foreach (string line in diff.Split('\n'))
        {
            // With -U0 every content line starts with '+', '-' or '\', so only headers start with "@@ ".
            if (!line.StartsWith("@@ ", StringComparison.Ordinal))
            {
                continue;
            }

            string[] parts = line.Split(' ');
            if (parts.Length < 3 || !parts[1].StartsWith('-') || !parts[2].StartsWith('+'))
            {
                continue;
            }

            (int oldStart, int oldCount) = ParseRange(parts[1]);
            (int newStart, int newCount) = ParseRange(parts[2]);
            hunks.Add(new DiffHunk(oldStart, oldCount, newStart, newCount));
        }

        return hunks;
    }

    private static (int Start, int Count) ParseRange(string range)
    {
        string[] numbers = range[1..].Split(',');
        int start = int.Parse(numbers[0], CultureInfo.InvariantCulture);
        int count = numbers.Length > 1 ? int.Parse(numbers[1], CultureInfo.InvariantCulture) : 1;
        return (start, count);
    }

    public static IReadOnlySet<int> Lines(IReadOnlyList<LineRange> ranges) =>
        ranges.SelectMany(r => Enumerable.Range(r.Start, r.End - r.Start + 1)).ToHashSet();
}
