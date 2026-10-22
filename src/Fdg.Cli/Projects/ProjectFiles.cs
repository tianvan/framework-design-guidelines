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
    /// leaving the git repository (a directory holding <c>.git</c>) the search started in.
    /// </summary>
    public static IReadOnlyList<string> FindOwningProjects(string directory)
    {
        for (DirectoryInfo? current = new(directory); current is not null; current = current.Parent)
        {
            string[] projects = Directory.GetFiles(current.FullName, "*.csproj");
            if (projects.Length > 0)
            {
                return projects.Order(StringComparer.OrdinalIgnoreCase).ToArray();
            }

            if (Path.Exists(Path.Combine(current.FullName, ".git")))
            {
                break;
            }
        }

        return [];
    }
}
