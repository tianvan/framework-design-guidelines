namespace Fdg.Cli.IO;

/// <summary>Checks both the spelling of a path and each existing component below the worktree root.</summary>
internal static class WorktreePath
{
    public static bool IsSafe(string root, string path)
    {
        string? relative = RelativePath(root, path);
        if (string.IsNullOrEmpty(relative))
        {
            return false;
        }

        string current = root;
        foreach (string part in relative.Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, part);
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    return false;
                }
            }
            catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
            {
                // A path that does not yet exist can be created under its checked parent.
            }
        }

        return true;
    }

    public static bool Contains(string root, string path) => RelativePath(root, path) is not null;

    private static string? RelativePath(string root, string path)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(root, path, comparison))
        {
            return string.Empty;
        }

        string prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, comparison) ? path[prefix.Length..] : null;
    }

    public static string Resolve(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string current = Path.GetPathRoot(fullPath)!;
        foreach (string part in fullPath[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            var directory = new DirectoryInfo(current);
            if (directory.LinkTarget is not null && directory.ResolveLinkTarget(returnFinalTarget: true) is { } target)
            {
                current = target.FullName;
            }
        }

        return current;
    }
}
