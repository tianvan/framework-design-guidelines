using System.Text;

namespace Fdg.Cli.Hooks;

/// <summary>
/// The candidate files gone from the working tree at a Stop: the files a new file may have been moved from. Their
/// start-of-turn content is read once, when the first new file needs it, and reused for the others.
/// </summary>
public sealed class MoveSources
{
    private readonly GitRepository _git;
    private readonly StateStore _state;
    private readonly TurnSnapshot _snapshot;
    private List<(byte[] Content, HashSet<string> Lines)>? _sources;

    /// <param name="deletedFiles">Relative paths, as <see cref="Snapshot.FindChangesAsync"/> returns them.</param>
    public MoveSources(GitRepository git, StateStore state, TurnSnapshot snapshot, IReadOnlyList<string> deletedFiles)
    {
        _git = git;
        _state = state;
        _snapshot = snapshot;
        DeletedFiles = deletedFiles;
    }

    public IReadOnlyList<string> DeletedFiles { get; }

    /// <returns>
    /// The start-of-turn content of the deleted file most of <paramref name="content"/>'s lines come from, if that is
    /// more than half of them; otherwise <see langword="null"/>.
    /// </returns>
    public async Task<byte[]?> FindAsync(byte[] content, CancellationToken cancellationToken)
    {
        if (DeletedFiles.Count == 0)
        {
            return null;
        }

        HashSet<string> lines = LineSet(content);
        if (lines.Count == 0)
        {
            return null;
        }

        byte[]? best = null;
        double bestOverlap = 0.5;
        foreach ((byte[] source, HashSet<string> sourceLines) in await LoadAsync(cancellationToken).ConfigureAwait(false))
        {
            double overlap = lines.Count(sourceLines.Contains) / (double)lines.Count;
            if (overlap > bestOverlap)
            {
                best = source;
                bestOverlap = overlap;
            }
        }

        return best;
    }

    private async Task<List<(byte[] Content, HashSet<string> Lines)>> LoadAsync(CancellationToken cancellationToken)
    {
        if (_sources is not null)
        {
            return _sources;
        }

        var sources = new List<(byte[], HashSet<string>)>();
        foreach (string deleted in DeletedFiles)
        {
            string copy = _state.BaselinePath(deleted);
            byte[]? content = _snapshot.DirtyFiles.ContainsKey(deleted) && File.Exists(copy)
                ? await File.ReadAllBytesAsync(copy, cancellationToken).ConfigureAwait(false)
                : await Snapshot.ReadCommittedAsync(_git, _snapshot, deleted, cancellationToken).ConfigureAwait(false);
            if (content is not null)
            {
                sources.Add((content, LineSet(content)));
            }
        }

        _sources = sources;
        return sources;
    }

    /// <summary>The distinct trimmed lines that hold a letter or digit, so braces and blank lines match nothing.</summary>
    private static HashSet<string> LineSet(byte[] content) =>
        Encoding.UTF8.GetString(content)
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Any(char.IsLetterOrDigit))
            .ToHashSet(StringComparer.Ordinal);
}
