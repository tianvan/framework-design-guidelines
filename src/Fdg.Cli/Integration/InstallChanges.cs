using System.Text.Json;
using Fdg.Cli.IO;
using Fdg.Cli.Tooling;

namespace Fdg.Cli.Integration;

internal sealed class InstallChanges(string root)
{
    private readonly List<FileChange> _changes = [];
    private readonly List<string> _notices = [];
    private readonly Dictionary<string, byte[]?> _reads = new(StringComparer.Ordinal);

    public int Count => _changes.Count;

    public void Notice(string notice) => _notices.Add(notice);

    public byte[]? Read(string relativePath)
    {
        if (!_reads.TryGetValue(relativePath, out byte[]? before))
        {
            before = ReadCurrent(root, relativePath);
            _reads.Add(relativePath, before);
        }

        return before;
    }

    public void Set(string relativePath, byte[]? after)
    {
        byte[]? before = Read(relativePath);
        if (!Equal(before, after))
        {
            _changes.Add(new FileChange(relativePath, before, after));
        }
    }

    public async Task PreviewDiffAsync(TextWriter output, CancellationToken cancellationToken)
    {
        if (_changes.Count == 0)
        {
            return;
        }

        var temporary = Directory.CreateTempSubdirectory("fdg-preview-");
        try
        {
            foreach (string name in new[] { "before", "after" })
            {
                string directory = Path.Combine(temporary.FullName, name);
                Directory.CreateDirectory(directory);
                foreach (FileChange change in _changes)
                {
                    byte[]? bytes = name == "before" ? change.Before : change.After;
                    if (bytes is not null)
                    {
                        string path = Path.Combine(directory, change.Path);
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        File.WriteAllBytes(path, bytes);
                    }
                }
            }

            ToolResult diff = await new ProcessRunner().RunAsync("git",
                ["diff", "--no-index", "--no-ext-diff", "--no-textconv", "--no-color", "--", "before", "after"],
                temporary.FullName, TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
            if (diff.ExitCode is not (0 or 1) || diff.TimedOut)
            {
                throw new CommandException("Could not display the FDG diff: " + diff.Output.Trim());
            }

            output.Write(diff.Output);
        }
        finally
        {
            temporary.Delete(recursive: true);
        }
    }

    public void Preview(TextWriter output)
    {
        foreach (string notice in _notices)
        {
            output.WriteLine("  " + notice);
        }

        foreach (FileChange change in _changes.OrderBy(c => c.Path, StringComparer.Ordinal))
        {
            char action = change.Before is null ? 'A' : change.After is null ? 'D' : 'M';
            output.WriteLine($"  {action} {change.Path}");
        }
    }

    public void Apply(string journal)
    {
        foreach ((string path, byte[]? before) in _reads)
        {
            if (!Equal(ReadCurrent(root, path), before))
            {
                throw new CommandException($"{path} changed during the preview. Run the command again to see the new plan.");
            }
        }

        string[] newDirectories = _changes.Where(c => c.After is not null)
            .SelectMany(c => Parents(c.Path))
            .Distinct(StringComparer.Ordinal)
            .Where(path => !Directory.Exists(Path.Combine(root, path)))
            .ToArray();
        var transaction = new Transaction(Guid.NewGuid(), _changes.ToArray(), newDirectories);
        AtomicFile.WriteAllText(journal, JsonSerializer.Serialize(transaction));
        try
        {
            foreach (FileChange change in _changes)
            {
                if (!Equal(ReadCurrent(root, change.Path), change.Before))
                {
                    throw new CommandException($"{change.Path} was edited during installation.");
                }

                Write(root, change.Path, change.After, transaction.Id);
            }

            File.Delete(journal);
        }
        catch
        {
            Recover(root, journal);
            throw;
        }

        RemoveEmptyDirectories(root, _changes.Where(c => c.After is null).SelectMany(c => Parents(c.Path)));
    }

    public static void Recover(string root, string journal)
    {
        if (!File.Exists(journal))
        {
            return;
        }

        Transaction transaction = JsonSerializer.Deserialize<Transaction>(File.ReadAllText(journal))
            ?? throw new CommandException($"Unreadable FDG installation transaction: {journal}.");
        foreach (FileChange change in transaction.Files)
        {
            byte[]? current = ReadCurrent(root, change.Path);
            if (!Equal(current, change.Before) && !Equal(current, change.After))
            {
                throw new CommandException($"Cannot recover the interrupted FDG installation because {change.Path} was edited. " +
                    $"Preserve your edit and restore that path to its before or after state from {journal}, then retry.");
            }
        }

        foreach (FileChange change in Enumerable.Reverse(transaction.Files))
        {
            string temporary = StagingPath(root, change.Path, transaction.Id);
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }

            if (!Equal(ReadCurrent(root, change.Path), change.Before))
            {
                Write(root, change.Path, change.Before, transaction.Id);
            }
        }

        RemoveEmptyDirectories(root, transaction.NewDirectories);
        File.Delete(journal);
    }

    internal static byte[]? ReadCurrent(string root, string path)
    {
        string fullPath = CheckedPath(root, path);
        try
        {
            return File.ReadAllBytes(fullPath);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    private static void Write(string root, string path, byte[]? bytes, Guid transaction)
    {
        string fullPath = CheckedPath(root, path);
        if (bytes is null)
        {
            File.Delete(fullPath);
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            AtomicFile.WriteAllBytesVia(CheckedPath(root, path), StagingPath(root, path, transaction), bytes);
        }
    }

    private static string StagingPath(string root, string path, Guid transaction)
    {
        string name = $".fdg-{transaction:N}-{Path.GetFileName(path)}.tmp";
        return CheckedPath(root, Path.Combine(Path.GetDirectoryName(path) ?? string.Empty, name));
    }

    private static string CheckedPath(string root, string path)
    {
        string fullPath = Path.Combine(root, path);
        if (!WorktreePath.IsSafe(root, fullPath) || Directory.Exists(fullPath) ||
            Parents(path).Any(parent => File.Exists(Path.Combine(root, parent))))
        {
            throw new CommandException($"Cannot manage {path}: expected a regular file inside this worktree with no symbolic links.");
        }

        return fullPath;
    }

    private static IEnumerable<string> Parents(string path)
    {
        for (string? parent = Path.GetDirectoryName(path); !string.IsNullOrEmpty(parent); parent = Path.GetDirectoryName(parent))
        {
            yield return parent;
        }
    }

    private static void RemoveEmptyDirectories(string root, IEnumerable<string> directories)
    {
        foreach (string path in directories.Distinct(StringComparer.Ordinal).OrderByDescending(path => path.Length))
        {
            string fullPath = Path.Combine(root, path);
            try
            {
                if (WorktreePath.IsSafe(root, fullPath) && Directory.Exists(fullPath) &&
                    !Directory.EnumerateFileSystemEntries(fullPath).Any())
                {
                    Directory.Delete(fullPath);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A concurrent editor or permission change can leave an empty directory for Git to ignore.
            }
        }
    }

    private static bool Equal(byte[]? left, byte[]? right) =>
        left is null ? right is null : right is not null && left.AsSpan().SequenceEqual(right);

    private sealed record FileChange(string Path, byte[]? Before, byte[]? After);

    private sealed record Transaction(Guid Id, FileChange[] Files, string[] NewDirectories);
}
