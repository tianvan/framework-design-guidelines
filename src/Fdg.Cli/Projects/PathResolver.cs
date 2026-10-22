using Fdg.Cli.Tooling;

namespace Fdg.Cli.Projects;

/// <summary>Turns the paths given to <c>dotnet fdg fix</c> into projects to fix, merged per project.</summary>
public static class PathResolver
{
    private static readonly TimeSpan SolutionListTimeout = TimeSpan.FromMinutes(2);

    /// <param name="paths">Directories, .csproj, .sln, .slnx or .cs files; none means <paramref name="cwd"/>.</param>
    /// <exception cref="CommandException">A path does not exist, has an unsupported type, or its solution cannot be listed.</exception>
    public static IReadOnlyList<FixTarget> Resolve(IReadOnlyList<string> paths, string cwd)
    {
        var order = new List<string>();
        var includes = new Dictionary<string, List<string>?>(ProjectFiles.PathComparer);

        void Add(string projectPath, string? include)
        {
            if (!includes.TryGetValue(projectPath, out List<string>? existing))
            {
                order.Add(projectPath);
                includes.Add(projectPath, include is null ? null : [include]);
            }
            else if (include is null)
            {
                includes[projectPath] = null;
            }
            else if (existing is not null && !existing.Contains(include, ProjectFiles.PathComparer))
            {
                existing.Add(include);
            }
        }

        foreach (string path in paths.Count == 0 ? [cwd] : paths)
        {
            string fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path, cwd));
            if (Directory.Exists(fullPath))
            {
                ResolveDirectory(fullPath, Add);
            }
            else if (File.Exists(fullPath))
            {
                ResolveFile(path, fullPath, Add);
            }
            else
            {
                throw new CommandException($"path not found: {path}");
            }
        }

        return order.Select(p => new FixTarget(p, includes[p])).ToArray();
    }

    private static void ResolveDirectory(string directory, Action<string, string?> add)
    {
        string[] nested = ProjectFiles.Enumerate(directory, "*.csproj").ToArray();
        foreach (string project in nested)
        {
            add(project, null);
        }

        if (Directory.EnumerateFiles(directory, "*.csproj").Any())
        {
            return;
        }

        // A directory inside a project: fix only that directory. Projects nested below it own their directories, so
        // the outer project then gets its own files under the directory instead.
        IReadOnlyList<string> owners = ProjectFiles.FindOwningProjects(directory);
        if (owners.Count == 0)
        {
            return;
        }

        string[] nestedDirectories = nested
            .Select(p => Path.GetDirectoryName(p)! + Path.DirectorySeparatorChar)
            .ToArray();
        IReadOnlyList<string> includes = nested.Length == 0
            ? [directory]
            : ProjectFiles.Enumerate(directory, "*.cs")
                .Where(f => !nestedDirectories.Any(d => f.StartsWith(d, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
        foreach (string owner in owners)
        {
            foreach (string include in includes)
            {
                add(owner, include);
            }
        }
    }

    private static void ResolveFile(string path, string fullPath, Action<string, string?> add)
    {
        switch (Path.GetExtension(fullPath).ToLowerInvariant())
        {
            case ".csproj":
                add(fullPath, null);
                break;
            case ".sln":
            case ".slnx":
                foreach (string project in ListSolutionProjects(fullPath))
                {
                    add(project, null);
                }

                break;
            case ".cs":
                foreach (string project in ProjectFiles.FindOwningProjects(Path.GetDirectoryName(fullPath)!))
                {
                    add(project, fullPath);
                }

                break;
            default:
                throw new CommandException($"not a directory, .csproj, .sln, .slnx or .cs file: {path}");
        }
    }

    /// <summary>The C# projects of a solution, from <c>dotnet sln &lt;file&gt; list</c>.</summary>
    private static IEnumerable<string> ListSolutionProjects(string solutionPath)
    {
        string solutionDirectory = Path.GetDirectoryName(solutionPath)!;
        ToolResult result = new ProcessRunner()
            .RunAsync("dotnet", ["sln", solutionPath, "list"], solutionDirectory, SolutionListTimeout, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        if (!result.Succeeded)
        {
            throw new CommandException(
                $"cannot list the projects of {solutionPath}:\n  " + string.Join("\n  ", result.FirstLines(20)));
        }

        // The header lines are localized; project lines are the ones naming a .csproj.
        return result.Output.ReplaceLineEndings("\n")
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Select(l => Path.GetFullPath(l.Replace('\\', Path.DirectorySeparatorChar), solutionDirectory));
    }
}
