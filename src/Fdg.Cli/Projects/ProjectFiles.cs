namespace Fdg.Cli.Projects;

/// <summary>Walks source trees the way the CLI scopes them: <c>bin/</c>, <c>obj/</c> and dot-directories are skipped.</summary>
public static class ProjectFiles
{
    public static StringComparer PathComparer { get; } =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static bool IsSkippedDirectory(string name) =>
        name.StartsWith('.') ||
        name.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("obj", StringComparison.OrdinalIgnoreCase);

    /// <summary>Files matching <paramref name="searchPattern"/> under <paramref name="directory"/>, in a stable order.</summary>
    public static IEnumerable<string> Enumerate(string directory, string searchPattern)
    {
        foreach (string file in Directory.EnumerateFiles(directory, searchPattern).Order(StringComparer.OrdinalIgnoreCase))
        {
            yield return file;
        }

        IEnumerable<string> children = Directory.EnumerateDirectories(directory)
            .Where(d => !IsSkippedDirectory(Path.GetFileName(d)))
            .Order(StringComparer.OrdinalIgnoreCase);
        foreach (string child in children)
        {
            foreach (string file in Enumerate(child, searchPattern))
            {
                yield return file;
            }
        }
    }

    /// <summary>
    /// The .csproj files in the nearest directory at or above <paramref name="directory"/> that has any, without
    /// leaving the git repository (a directory holding <c>.git</c>) the search started in, nor going above
    /// <paramref name="stopAt"/>, which is searched itself.
    /// </summary>
    /// <param name="stopAt">
    /// Where the search ends outside git; by default the user's home directory, so that a file in a directory that
    /// is not a repository is not given to a project far above it. Ignored when <paramref name="directory"/> is not
    /// at or below it.
    /// </param>
    public static IReadOnlyList<string> FindOwningProjects(string directory, string? stopAt = null)
    {
        string? boundary = Boundary(directory, stopAt ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        for (DirectoryInfo? current = new(directory); current is not null; current = current.Parent)
        {
            string[] projects = Directory.GetFiles(current.FullName, "*.csproj");
            if (projects.Length > 0)
            {
                return projects.Order(StringComparer.OrdinalIgnoreCase).ToArray();
            }

            if (Path.Exists(Path.Combine(current.FullName, ".git")) ||
                (boundary is not null && PathComparer.Equals(Path.TrimEndingDirectorySeparator(current.FullName), boundary)))
            {
                break;
            }
        }

        return [];
    }

    /// <returns>
    /// <paramref name="stopAt"/> as a full path without a trailing separator, or <see langword="null"/> when it is
    /// empty or <paramref name="directory"/> is not at or below it.
    /// </returns>
    private static string? Boundary(string directory, string stopAt)
    {
        if (string.IsNullOrWhiteSpace(stopAt))
        {
            return null;
        }

        string boundary = Path.TrimEndingDirectorySeparator(Path.GetFullPath(stopAt));
        string start = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        if (PathComparer.Equals(start, boundary))
        {
            return boundary;
        }

        // A root such as C:\ keeps its separator.
        string prefix = Path.EndsInDirectorySeparator(boundary) ? boundary : boundary + Path.DirectorySeparatorChar;
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return start.StartsWith(prefix, comparison) ? boundary : null;
    }
}
