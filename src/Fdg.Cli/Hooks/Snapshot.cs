using System.Security.Cryptography;
using Fdg.Cli.Projects;

namespace Fdg.Cli.Hooks;

/// <summary>The state of the working tree when a turn started.</summary>
/// <param name="Turn">Counts the turns of the session, starting at 1.</param>
/// <param name="Head">The commit HEAD pointed to, or <see langword="null"/> in a repository without commits.</param>
/// <param name="DirtyFiles">Candidate files that differed from HEAD, with the SHA-256 of their content.</param>
/// <param name="Started">
/// When the turn started; <see cref="StateStore.ReadSnapshot"/> fills it in for snapshots written without it.
/// </param>
public sealed record TurnSnapshot(
    int Turn,
    string? Head,
    IReadOnlyDictionary<string, string> DirtyFiles,
    DateTimeOffset? Started = null);

/// <summary>
/// What <see cref="Snapshot.FindChangesAsync"/> found: changed candidate files, deleted candidate paths, and one-line
/// notes about changes it could not attribute to the turn.
/// </summary>
public sealed record TurnChanges(IReadOnlyList<string> Changed, IReadOnlyList<string> Deleted, IReadOnlyList<string> Notes);

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
        DateTimeOffset started = DateTimeOffset.UtcNow;
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

        var snapshot = new TurnSnapshot(turn, head, dirty, started);
        state.WriteSnapshot(snapshot);
        return snapshot;
    }

    /// <summary>Candidate files whose content differs from the start of the turn, in ordinal order.</summary>
    public static async Task<IReadOnlyList<string>> FindChangedFilesAsync(
        GitRepository git,
        TurnSnapshot snapshot,
        CancellationToken cancellationToken) =>
        (await FindChangesAsync(git, snapshot, cancellationToken).ConfigureAwait(false)).Changed;

    /// <summary>
    /// From one <c>git status</c>: the candidate files whose content differs from the start of the turn, and the
    /// candidate paths gone from the working tree (deleted, or moved away, since HEAD or since the turn started),
    /// each in ordinal order. When HEAD moved during the turn, as when the AI commits, the files of the commits made
    /// during the turn count as well (<see cref="FindCommittedAsync"/>); their baseline stays the start of the turn.
    /// </summary>
    public static async Task<TurnChanges> FindChangesAsync(
        GitRepository git,
        TurnSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<GitStatusEntry> entries = await git.StatusAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<string> committed = [];
        var notes = new List<string>();
        if (await git.HeadAsync(cancellationToken).ConfigureAwait(false) is string head && head != snapshot.Head)
        {
            (committed, string? note) = await FindCommittedAsync(git, snapshot, head, cancellationToken).ConfigureAwait(false);
            if (note is not null)
            {
                notes.Add(note);
            }
        }

        IReadOnlyList<string> paths = entries.Select(e => e.Path).Concat(committed).ToArray();
        var changed = new List<string>();
        foreach (string path in Candidates(git, paths))
        {
            if (!snapshot.DirtyFiles.TryGetValue(path, out string? hash) ||
                hash != Hash(await File.ReadAllBytesAsync(FullPath(git, path), cancellationToken).ConfigureAwait(false)))
            {
                changed.Add(path);
            }
        }

        string[] deleted = paths
            .Concat(snapshot.DirtyFiles.Keys)
            .Where(IsCandidatePath)
            .Where(p => !File.Exists(FullPath(git, p)))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        return new TurnChanges(changed, deleted, notes);
    }

    /// <summary>
    /// The files that the non-merge commits made during the turn (committed at or after its start) changed, and
    /// that still differ between the turn's HEAD and <paramref name="head"/>. Commits from before the turn, brought
    /// in by a pull, a checkout or a rebase, are someone else's: their files are left out, with a note.
    /// </summary>
    private static async Task<(IReadOnlyList<string> Files, string? Note)> FindCommittedAsync(
        GitRepository git,
        TurnSnapshot snapshot,
        string head,
        CancellationToken cancellationToken)
    {
        const string HeadMoved = "FDG: HEAD moved during the turn by commits not made in it; their files were not checked.";
        try
        {
            bool foreign = snapshot.Head is not null &&
                !await git.IsAncestorAsync(snapshot.Head, head, cancellationToken).ConfigureAwait(false);
            // Commit times have whole seconds. Without a start time, no commit counts as the turn's.
            long started = snapshot.Started?.ToUnixTimeSeconds() ?? long.MaxValue;
            IReadOnlyList<GitCommit> commits = await git.ListCommitsAsync(snapshot.Head, head, cancellationToken)
                .ConfigureAwait(false);
            foreign |= commits.Any(c => c.CommitTime < started);
            string[] made = commits.Where(c => c.ParentCount < 2 && c.CommitTime >= started).Select(c => c.Hash).ToArray();
            if (made.Length == 0)
            {
                return ([], foreign ? HeadMoved : null);
            }

            string from = snapshot.Head ?? await git.EmptyTreeAsync(cancellationToken).ConfigureAwait(false);
            IReadOnlyList<string> differ = await git.DiffNamesAsync(from, head, cancellationToken).ConfigureAwait(false);
            IReadOnlyList<string> files = await git.CommitFilesAsync(made, cancellationToken).ConfigureAwait(false);
            return (files.Intersect(differ, StringComparer.Ordinal).ToArray(), foreign ? HeadMoved : null);
        }
        catch (InvalidOperationException e)
        {
            string reason = e.Message.Split('\n', 2)[0].Trim();
            return ([], $"FDG: cannot list the commits made during the turn ({reason}); their files were not checked.");
        }
    }

    /// <inheritdoc cref="WriteBaselineAsync(GitRepository, StateStore, TurnSnapshot, string, MoveSources?, CancellationToken)"/>
    public static Task<string> WriteBaselineAsync(
        GitRepository git,
        StateStore state,
        TurnSnapshot snapshot,
        string relativePath,
        CancellationToken cancellationToken) =>
        WriteBaselineAsync(git, state, snapshot, relativePath, null, cancellationToken);

    /// <summary>
    /// Writes the start-of-turn content of a changed file to the state directory, if it is not there yet: a dirty
    /// file was copied at the start; a clean one is HEAD's content as a checkout would write it. A new file takes
    /// the start-of-turn content of the deleted file it was moved from (<see cref="MoveSources.FindAsync"/>). Any
    /// other new file, or any file in a repository without commits, is empty.
    /// </summary>
    /// <returns>The baseline file's path.</returns>
    public static async Task<string> WriteBaselineAsync(
        GitRepository git,
        StateStore state,
        TurnSnapshot snapshot,
        string relativePath,
        MoveSources? moveSources,
        CancellationToken cancellationToken)
    {
        string baseline = state.BaselinePath(relativePath);
        if (snapshot.DirtyFiles.ContainsKey(relativePath) && File.Exists(baseline))
        {
            return baseline;
        }

        byte[]? content = await ReadCommittedAsync(git, snapshot, relativePath, cancellationToken).ConfigureAwait(false);
        if (content is null && moveSources is not null)
        {
            byte[] current = await File.ReadAllBytesAsync(FullPath(git, relativePath), cancellationToken).ConfigureAwait(false);
            content = await moveSources.FindAsync(current, cancellationToken).ConfigureAwait(false);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(baseline)!);
        await File.WriteAllBytesAsync(baseline, content ?? [], cancellationToken).ConfigureAwait(false);
        return baseline;
    }

    /// <returns>The file's content at the turn's HEAD, or <see langword="null"/> without commits or such a file.</returns>
    internal static async Task<byte[]?> ReadCommittedAsync(
        GitRepository git,
        TurnSnapshot snapshot,
        string relativePath,
        CancellationToken cancellationToken) =>
        snapshot.Head is null
            ? null
            : await git.ReadCommittedFileAsync(snapshot.Head, relativePath, cancellationToken).ConfigureAwait(false);

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

    private static async Task<IReadOnlyList<string>> CandidatesAsync(GitRepository git, CancellationToken cancellationToken) =>
        Candidates(git, (await git.StatusAsync(cancellationToken).ConfigureAwait(false)).Select(e => e.Path));

    private static IReadOnlyList<string> Candidates(GitRepository git, IEnumerable<string> paths) =>
        paths
            .Where(IsCandidatePath)
            .Where(p => File.Exists(FullPath(git, p)))
            .Where(p => ProjectFiles.FindOwningProjects(Path.GetDirectoryName(FullPath(git, p))!).Count > 0)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static string Hash(byte[] content) => Convert.ToHexString(SHA256.HashData(content));
}
