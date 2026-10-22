using System.Security.Cryptography;
using System.Text;
using Fdg.Cli.Projects;

namespace Fdg.Cli.Hooks;

/// <summary>The state of the working tree when a turn started.</summary>
/// <param name="Turn">Counts the turns of the session, starting at 1.</param>
/// <param name="Head">The commit HEAD pointed to, or <see langword="null"/> in a repository without commits.</param>
/// <param name="DirtyFiles">Candidate files that differed from HEAD, with the SHA-256 of their content.</param>
public sealed record TurnSnapshot(int Turn, string? Head, IReadOnlyDictionary<string, string> DirtyFiles);

/// <summary>
/// Turn snapshots from <c>git status</c>. A file changed in a turn is a candidate file that is dirty now and was
/// either clean at the start or had different content, so edits made through a shell are found as well.
/// Candidates are cheap to find: <c>.cs</c> files outside <c>bin/</c>, <c>obj/</c> and dot-directories that have
/// an owning project. Whether that project is in scope is decided by the Stop hook.
/// </summary>
public static class Snapshot
{
    public static async Task<TurnSnapshot> TakeAsync(GitRepository git, StateStore state, CancellationToken cancellationToken)
    {
        int turn = (state.ReadSnapshot()?.Turn ?? 0) + 1;
        state.ResetTurn();
        string? head = await git.HeadAsync(cancellationToken).ConfigureAwait(false);
        var dirty = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string path in await CandidatesAsync(git, cancellationToken).ConfigureAwait(false))
        {
            byte[] content = await File.ReadAllBytesAsync(FullPath(git, path), cancellationToken).ConfigureAwait(false);
            string baseline = state.BaselinePath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(baseline)!);
            await File.WriteAllBytesAsync(baseline, content, cancellationToken).ConfigureAwait(false);
            dirty.Add(path, Hash(content));
        }

        var snapshot = new TurnSnapshot(turn, head, dirty);
        state.WriteSnapshot(snapshot);
        return snapshot;
    }

    /// <summary>Candidate files whose content differs from the start of the turn, in ordinal order.</summary>
    public static async Task<IReadOnlyList<string>> FindChangedFilesAsync(
        GitRepository git,
        TurnSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var changed = new List<string>();
        foreach (string path in await CandidatesAsync(git, cancellationToken).ConfigureAwait(false))
        {
            if (!snapshot.DirtyFiles.TryGetValue(path, out string? hash) ||
                hash != Hash(await File.ReadAllBytesAsync(FullPath(git, path), cancellationToken).ConfigureAwait(false)))
            {
                changed.Add(path);
            }
        }

        return changed;
    }

    /// <summary>Candidate paths that are gone from the working tree: deleted, or moved away, since HEAD or since the turn started.</summary>
    public static async Task<IReadOnlyList<string>> FindDeletedFilesAsync(
        GitRepository git,
        TurnSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<GitStatusEntry> entries = await git.StatusAsync(cancellationToken).ConfigureAwait(false);
        return entries
            .Select(e => e.Path)
            .Concat(snapshot.DirtyFiles.Keys)
            .Where(IsCandidatePath)
            .Where(p => !File.Exists(FullPath(git, p)))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    /// <inheritdoc cref="WriteBaselineAsync(GitRepository, StateStore, TurnSnapshot, string, IReadOnlyList{string}, CancellationToken)"/>
    public static Task<string> WriteBaselineAsync(
        GitRepository git,
        StateStore state,
        TurnSnapshot snapshot,
        string relativePath,
        CancellationToken cancellationToken) =>
        WriteBaselineAsync(git, state, snapshot, relativePath, [], cancellationToken);

    /// <summary>
    /// Writes the start-of-turn content of a changed file to the state directory, if it is not there yet: a dirty
    /// file was copied at the start; a clean one is HEAD's content as a checkout would write it. A new file takes
    /// the start-of-turn content of the deleted file it was moved from: the one most of its lines come from, if
    /// that is more than half. Any other new file, or any file in a repository without commits, is empty.
    /// </summary>
    /// <param name="deletedFiles">From <see cref="FindDeletedFilesAsync"/>.</param>
    /// <returns>The baseline file's path.</returns>
    public static async Task<string> WriteBaselineAsync(
        GitRepository git,
        StateStore state,
        TurnSnapshot snapshot,
        string relativePath,
        IReadOnlyList<string> deletedFiles,
        CancellationToken cancellationToken)
    {
        string baseline = state.BaselinePath(relativePath);
        if (snapshot.DirtyFiles.ContainsKey(relativePath) && File.Exists(baseline))
        {
            return baseline;
        }

        byte[] content = await ReadCommittedAsync(git, snapshot, relativePath, cancellationToken).ConfigureAwait(false)
            ?? await FindMovedFromAsync(git, state, snapshot, relativePath, deletedFiles, cancellationToken).ConfigureAwait(false)
            ?? [];
        Directory.CreateDirectory(Path.GetDirectoryName(baseline)!);
        await File.WriteAllBytesAsync(baseline, content, cancellationToken).ConfigureAwait(false);
        return baseline;
    }

    private static async Task<byte[]?> ReadCommittedAsync(
        GitRepository git,
        TurnSnapshot snapshot,
        string relativePath,
        CancellationToken cancellationToken) =>
        snapshot.Head is null
            ? null
            : await git.ReadCommittedFileAsync(snapshot.Head, relativePath, cancellationToken).ConfigureAwait(false);

    /// <returns>The start-of-turn content of the best-matching deleted file, or <see langword="null"/>.</returns>
    private static async Task<byte[]?> FindMovedFromAsync(
        GitRepository git,
        StateStore state,
        TurnSnapshot snapshot,
        string relativePath,
        IReadOnlyList<string> deletedFiles,
        CancellationToken cancellationToken)
    {
        if (deletedFiles.Count == 0)
        {
            return null;
        }

        HashSet<string> lines = LineSet(await File.ReadAllBytesAsync(FullPath(git, relativePath), cancellationToken).ConfigureAwait(false));
        byte[]? best = null;
        double bestOverlap = 0.5;
        foreach (string deleted in deletedFiles)
        {
            string copy = state.BaselinePath(deleted);
            byte[]? content = snapshot.DirtyFiles.ContainsKey(deleted) && File.Exists(copy)
                ? await File.ReadAllBytesAsync(copy, cancellationToken).ConfigureAwait(false)
                : await ReadCommittedAsync(git, snapshot, deleted, cancellationToken).ConfigureAwait(false);
            if (content is null || lines.Count == 0)
            {
                continue;
            }

            double overlap = lines.Count(LineSet(content).Contains) / (double)lines.Count;
            if (overlap > bestOverlap)
            {
                best = content;
                bestOverlap = overlap;
            }
        }

        return best;
    }

    /// <summary>The distinct trimmed lines that hold a letter or digit, so braces and blank lines match nothing.</summary>
    private static HashSet<string> LineSet(byte[] content) =>
        Encoding.UTF8.GetString(content)
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Any(char.IsLetterOrDigit))
            .ToHashSet(StringComparer.Ordinal);

    public static string FullPath(GitRepository git, string relativePath) =>
        Path.GetFullPath(Path.Combine(git.TopLevel, relativePath));

    /// <summary>A <c>.cs</c> file outside <c>bin/</c>, <c>obj/</c> and dot-directories.</summary>
    public static bool IsCandidatePath(string relativePath)
    {
        if (!relativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string[] segments = relativePath.Split('/');
        return !segments[..^1].Any(ProjectFiles.IsSkippedDirectory);
    }

    private static async Task<IReadOnlyList<string>> CandidatesAsync(GitRepository git, CancellationToken cancellationToken)
    {
        IReadOnlyList<GitStatusEntry> entries = await git.StatusAsync(cancellationToken).ConfigureAwait(false);
        return entries
            .Select(e => e.Path)
            .Where(IsCandidatePath)
            .Where(p => File.Exists(FullPath(git, p)))
            .Where(p => ProjectFiles.FindOwningProjects(Path.GetDirectoryName(FullPath(git, p))!).Count > 0)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static string Hash(byte[] content) => Convert.ToHexString(SHA256.HashData(content));
}
