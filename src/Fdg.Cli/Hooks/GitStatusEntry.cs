namespace Fdg.Cli.Hooks;

/// <summary>One entry of <c>git status --porcelain=v1 -z</c>.</summary>
/// <param name="Status">The two status letters, such as <c>" M"</c>, <c>"A "</c> or <c>"??"</c>.</param>
/// <param name="Path">Relative to the repository root, with forward slashes.</param>
public sealed record GitStatusEntry(string Status, string Path)
{
    /// <summary>
    /// Entries are <c>XY path</c> terminated by NUL; a rename or copy is followed by its source path, which is
    /// skipped. With <c>-z</c> paths are never quoted, so spaces and non-ASCII characters arrive as they are.
    /// </summary>
    public static IReadOnlyList<GitStatusEntry> Parse(string output)
    {
        var entries = new List<GitStatusEntry>();
        string[] fields = output.Split('\0');
        for (int i = 0; i < fields.Length; i++)
        {
            string field = fields[i];
            if (field.Length < 4 || field[2] != ' ')
            {
                continue;
            }

            string status = field[..2];
            entries.Add(new GitStatusEntry(status, field[3..]));
            if (status.Contains('R') || status.Contains('C'))
            {
                i++;
            }
        }

        return entries;
    }
}
