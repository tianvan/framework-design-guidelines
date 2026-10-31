using System.Xml;
using System.Xml.Linq;

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

    /// <summary>
    /// The full paths of the files the project file itself lists in static <c>&lt;Compile Include="..."&gt;</c> items,
    /// such as files linked from a shared directory. Relative paths are taken from the project directory;
    /// <c>$(MSBuildThisFileDirectory)</c> and <c>$(MSBuildProjectDirectory)</c> are the project directory too.
    /// </summary>
    /// <remarks>
    /// Read from the XML, not evaluated: values with wildcards, other properties, item references or metadata are
    /// left out, and so are items from imported files such as Directory.Build.props and items under a condition
    /// that may not hold. An unreadable project file lists nothing.
    /// </remarks>
    public static IReadOnlyList<string> StaticCompileItems(string projectPath)
    {
        XDocument document;
        try
        {
            document = XDocument.Load(projectPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or XmlException)
        {
            return [];
        }

        string directory = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
        var files = new List<string>();
        foreach (XElement item in document.Descendants().Where(e => e.Name.LocalName == "Compile"))
        {
            if (item.Attribute("Include")?.Value is not { } include ||
                item.Attribute("Condition") is not null ||
                item.Ancestors().Any(a => a.Attribute("Condition") is not null))
            {
                continue;
            }

            foreach (string value in include.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                string path = WithoutPrefix(WithoutPrefix(value, "$(MSBuildThisFileDirectory)"), "$(MSBuildProjectDirectory)");
                if (path.IndexOfAny(['*', '?', '$', '@', '%']) >= 0)
                {
                    continue;
                }

                try
                {
                    files.Add(Path.GetFullPath(Path.Combine(directory, path.Replace('\\', '/'))));
                }
                catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
                {
                }
            }
        }

        return files;
    }

    /// <summary>Removes a leading property that stands for the project directory, and the separator after it.</summary>
    private static string WithoutPrefix(string value, string property) =>
        value.StartsWith(property, StringComparison.OrdinalIgnoreCase)
            ? value[property.Length..].TrimStart('/', '\\')
            : value;

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
