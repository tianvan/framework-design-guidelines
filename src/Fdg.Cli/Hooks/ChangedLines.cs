using System.Globalization;

namespace Fdg.Cli.Hooks;

/// <summary>Lines <see cref="Start"/> through <see cref="End"/>, 1-based and inclusive.</summary>
public sealed record LineRange(int Start, int End);

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

    /// <summary>The new-side ranges of the <c>@@ -a,b +c,d @@</c> headers; a missing <c>d</c> means 1.</summary>
    public static IReadOnlyList<LineRange> ParseHunks(string diff)
    {
        var ranges = new List<LineRange>();
        foreach (string line in diff.Split('\n'))
        {
            // With -U0 every content line starts with '+', '-' or '\', so only headers start with "@@ ".
            if (!line.StartsWith("@@ ", StringComparison.Ordinal))
            {
                continue;
            }

            string[] parts = line.Split(' ');
            string? added = parts.FirstOrDefault(p => p.StartsWith('+'));
            if (added is null)
            {
                continue;
            }

            string[] numbers = added[1..].Split(',');
            int start = int.Parse(numbers[0], CultureInfo.InvariantCulture);
            int count = numbers.Length > 1 ? int.Parse(numbers[1], CultureInfo.InvariantCulture) : 1;
            if (count > 0)
            {
                ranges.Add(new LineRange(start, start + count - 1));
            }
        }

        return ranges;
    }

    public static IReadOnlySet<int> Lines(IReadOnlyList<LineRange> ranges) =>
        ranges.SelectMany(r => Enumerable.Range(r.Start, r.End - r.Start + 1)).ToHashSet();
}
