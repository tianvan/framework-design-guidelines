using System.Xml;
using System.Xml.Linq;

namespace Fdg.Cli.Projects;

/// <summary>
/// The projects a project references, read statically from the <c>Include</c> of its <c>ProjectReference</c> items,
/// so that the Stop hook can tell whether a turn changed any of them without evaluating MSBuild.
/// </summary>
public static class ProjectReferences
{
    /// <summary>
    /// The full paths of the projects <paramref name="projectPath"/> references, directly or through each other, without
    /// the project itself. Every <c>ProjectReference</c> counts, whatever its <c>Condition</c>, so the set may hold
    /// more than a build uses but never less, as far as static reading goes.
    /// </summary>
    /// <returns>
    /// <see langword="null"/> when the closure is not known: an <c>Include</c> holds an MSBuild property other than
    /// <c>$(MSBuildThisFileDirectory)</c> or <c>$(MSBuildProjectDirectory)</c>, an item or metadata reference, a
    /// wildcard or an escape; a project in the closure cannot be read; or the nearest <c>Directory.Build.props</c> or
    /// <c>Directory.Build.targets</c> of a project in the closure has a <c>ProjectReference</c> of its own. Project
    /// files imported explicitly are not read.
    /// </returns>
    public static IReadOnlySet<string>? Closure(string projectPath)
    {
        string root = Path.GetFullPath(projectPath);
        var closure = new HashSet<string>(ProjectFiles.PathComparer);
        var directoryFiles = new Dictionary<string, bool>(ProjectFiles.PathComparer);
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            string project = pending.Pop();
            IReadOnlyList<string>? references = Read(project);
            if (references is null || HasDirectoryFileReferences(project, directoryFiles))
            {
                return null;
            }

            foreach (string reference in references)
            {
                if (!ProjectFiles.PathComparer.Equals(reference, root) && closure.Add(reference))
                {
                    pending.Push(reference);
                }
            }
        }

        return closure;
    }

    /// <returns>The full paths of the project's references, or <see langword="null"/> when they are not known.</returns>
    private static List<string>? Read(string projectPath)
    {
        XDocument document;
        try
        {
            document = XDocument.Load(projectPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or XmlException)
        {
            return null;
        }

        string directory = Path.GetDirectoryName(projectPath)!;
        var references = new List<string>();
        foreach (XElement item in document.Descendants().Where(e => e.Name.LocalName == "ProjectReference"))
        {
            if (item.Attribute("Include")?.Value is not { } include)
            {
                // An Update or Remove changes an item that some Include added.
                continue;
            }

            foreach (string value in include.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                string path = WithoutDirectoryProperty(value);
                if (path.IndexOfAny(['*', '?', '$', '@', '%']) >= 0)
                {
                    return null;
                }

                try
                {
                    references.Add(Path.GetFullPath(Path.Combine(directory, path.Replace('\\', '/'))));
                }
                catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
                {
                    return null;
                }
            }
        }

        return references;
    }

    /// <summary>
    /// Whether the <c>Directory.Build.props</c> or <c>Directory.Build.targets</c> MSBuild imports into the project, the
    /// nearest one at or above its directory, has a <c>ProjectReference</c> or cannot be read.
    /// </summary>
    private static bool HasDirectoryFileReferences(string projectPath, Dictionary<string, bool> cache)
    {
        foreach (string name in (string[])["Directory.Build.props", "Directory.Build.targets"])
        {
            for (string? directory = Path.GetDirectoryName(projectPath); directory is not null; directory = Path.GetDirectoryName(directory))
            {
                string file = Path.Combine(directory, name);
                if (!File.Exists(file))
                {
                    continue;
                }

                if (!cache.TryGetValue(file, out bool hasReferences))
                {
                    hasReferences = HasReferences(file);
                    cache.Add(file, hasReferences);
                }

                if (hasReferences)
                {
                    return true;
                }

                break;
            }
        }

        return false;
    }

    private static bool HasReferences(string file)
    {
        try
        {
            return XDocument.Load(file).Descendants().Any(e => e.Name.LocalName == "ProjectReference");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or XmlException)
        {
            return true;
        }
    }

    /// <summary>
    /// The value relative to the project directory: a leading <c>$(MSBuildThisFileDirectory)</c> or
    /// <c>$(MSBuildProjectDirectory)</c>, which in a project file both name its directory, is dropped with the
    /// separator after it.
    /// </summary>
    private static string WithoutDirectoryProperty(string value)
    {
        foreach (string property in (string[])["$(MSBuildThisFileDirectory)", "$(MSBuildProjectDirectory)"])
        {
            if (value.StartsWith(property, StringComparison.OrdinalIgnoreCase))
            {
                return value[property.Length..].TrimStart('/', '\\');
            }
        }

        return value;
    }
}
