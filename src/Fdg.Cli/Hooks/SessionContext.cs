using System.Text;
using Fdg.Cli.Commands;
using Fdg.Cli.Projects;
using Fdg.Cli.Tooling;

namespace Fdg.Cli.Hooks;

/// <summary>What SessionStart tells the AI: the context lines, then one line per setup issue.</summary>
public sealed record SessionReport(string Context, IReadOnlyList<string> Issues)
{
    public string Text => Issues.Count == 0 ? Context : Context + "\n" + string.Join("\n", Issues);
}

/// <summary>
/// The SessionStart context (design §5.5) and self-check. Facts come from the files only: project files, the
/// Directory.Build.props above them and the analyzer package's <c>obj/fdg/marker*.json</c>; MSBuild is never run.
/// </summary>
public sealed class SessionContext
{
    private const string Header = "This repository follows Framework Design Guidelines (3rd ed.).";

    private const string StyleLine =
        "- Write in .NET BCL style: Allman braces, one statement per line, long parameter/argument lists chopped before " +
        "the first item, one per line.";

    private const string AutofixLine =
        "- Layout and machine-checkable rules are auto-fixed and checked at the end of each turn; only fix the FDG/CA/IDE " +
        "issues reported to you.";

    private const string SkillsLine =
        "- Before designing or changing types, members, or public APIs, use the fdg-write skill; use fdg-review to review " +
        "an API.";

    private static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(30);

    private static readonly string[] Profiles = ["library", "application", "test"];

    private static readonly string[] Pathspecs = [":(glob,icase)**/*.csproj", ":(glob,icase)**/Directory.Build.props"];

    private readonly IProcessRunner _runner;

    /// <param name="runner">Runs <c>dotnet --version</c>.</param>
    public SessionContext(IProcessRunner runner)
    {
        _runner = runner;
    }

    /// <param name="repoRoot">The git top level; outside git, the session's directory.</param>
    /// <returns>The text to add to the AI's context, or <see langword="null"/> when no project uses the analyzers.</returns>
    public string? Build(string repoRoot) => Inspect(repoRoot)?.Text;

    /// <inheritdoc cref="Build"/>
    public SessionReport? Inspect(string repoRoot)
    {
        if (!Directory.Exists(repoRoot))
        {
            return null;
        }

        GitRepository? git = GitRepository.OpenAsync(repoRoot, CancellationToken.None).GetAwaiter().GetResult();
        string root = git?.TopLevel ?? Path.GetFullPath(repoRoot);
        IReadOnlyList<string> files = git is null
            ? ListNearbyFiles(root)
            : git.ListFilesAsync(Pathspecs, CancellationToken.None).GetAwaiter().GetResult();

        List<Project> projects = Discover(root, files);
        var onboarded = projects.Where(p => p.SdkStyle && p.Onboarded).ToList();
        if (onboarded.Count == 0)
        {
            return null;
        }

        var noProfile = new List<string>();
        var neverBuilt = new List<string>();
        var newerProtocol = new List<string>();
        var olderProtocol = new List<string>();
        var langVersions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var frameworks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Project project in onboarded)
        {
            ProjectMarker[] markers = OwnMarkers(root, project.RelativePath);
            if (markers.Length == 0)
            {
                neverBuilt.Add(project.RelativePath);
                continue;
            }

            string? profile = markers.Select(m => m.Profile?.Trim().ToLowerInvariant()).FirstOrDefault(p => !string.IsNullOrEmpty(p));
            if (profile is not null && Profiles.Contains(profile))
            {
                project.Profile = profile;
            }
            else
            {
                noProfile.Add(project.RelativePath);
            }

            if (markers.Any(m => m.Protocol > FixCommand.SupportedProtocol))
            {
                newerProtocol.Add(project.RelativePath);
            }
            else if (markers.Any(m => m.Protocol < FixCommand.SupportedProtocol))
            {
                olderProtocol.Add(project.RelativePath);
            }

            foreach (ProjectMarker marker in markers)
            {
                if (LangVersion(marker.LangVersion) is string langVersion)
                {
                    langVersions.Add(langVersion);
                }

                if (!string.IsNullOrWhiteSpace(marker.TargetFramework))
                {
                    frameworks.Add(marker.TargetFramework.Trim());
                }
            }
        }

        var context = new StringBuilder();
        context.Append(Header).Append('\n');
        context.Append(ProfilesLine(projects, langVersions, frameworks)).Append('\n');
        context.Append(StyleLine).Append('\n');
        context.Append(AutofixLine).Append('\n');
        context.Append(SkillsLine);
        var facts = new SetupFacts(
            DotnetVersion(root),
            noProfile,
            neverBuilt,
            newerProtocol,
            olderProtocol,
            projects.Count(p => !p.SdkStyle),
            git is not null);
        return new SessionReport(context.ToString(), SelfCheck.Issues(facts));
    }

    /// <summary>The project's markers; one written by another project in the same directory is not its own.</summary>
    private static ProjectMarker[] OwnMarkers(string root, string relativePath)
    {
        string projectDirectory = Path.GetDirectoryName(Path.Combine(root, relativePath))!;
        // By file name, so markers stay valid when the repository moves.
        string fileName = Path.GetFileName(relativePath);
        return ProjectMarker.ReadAll(Path.Combine(projectDirectory, "obj", "fdg"))
            .Where(m => string.IsNullOrEmpty(m.ProjectPath) ||
                ProjectFiles.PathComparer.Equals(Path.GetFileName(m.ProjectPath.Replace('\\', '/')), fileName))
            .ToArray();
    }

    /// <summary>
    /// Outside git, looks two directory levels down only: sessions start anywhere, such as a home directory, where a
    /// full walk is slow and meets junctions that deny listing.
    /// </summary>
    private static IReadOnlyList<string> ListNearbyFiles(string root)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            MaxRecursionDepth = 2,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };
        return Directory.EnumerateFiles(root, "*", options)
            .Where(f => IsProjectFile(f) || IsBuildProps(f))
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .ToArray();
    }

    /// <param name="files">Paths relative to <paramref name="root"/>, with forward slashes.</param>
    private static List<Project> Discover(string root, IReadOnlyList<string> files)
    {
        // bin/, obj/ and dot-directories hold no projects of the repository's own.
        string[] inScope = files
            .Where(f => !f.Split('/')[..^1].Any(ProjectFiles.IsSkippedDirectory))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var buildProps = new Dictionary<string, bool>(ProjectFiles.PathComparer);
        foreach (string props in inScope.Where(IsBuildProps))
        {
            buildProps[DirectoryOf(props)] = MentionsPackage(Path.Combine(root, props));
        }

        var projects = new List<Project>();
        foreach (string relativePath in inScope.Where(IsProjectFile))
        {
            string fullPath = Path.Combine(root, relativePath);
            if (!File.Exists(fullPath))
            {
                // Tracked but deleted in the working tree.
                continue;
            }

            bool onboarded = MentionsPackage(fullPath) || Ancestors(relativePath).Any(d => buildProps.GetValueOrDefault(d));
            projects.Add(new Project(relativePath, IsSdkStyle(fullPath), onboarded));
        }

        return projects;
    }

    /// <summary>
    /// <c>- Profiles: src/** = application, tests/** = test. C# 7.3; targets net472, netstandard2.0. ...</c>
    /// </summary>
    private static string ProfilesLine(
        IReadOnlyList<Project> projects,
        IReadOnlySet<string> langVersions,
        IReadOnlySet<string> frameworks)
    {
        var entries = new List<string>();
        Collapse(string.Empty, projects, entries);
        var line = new StringBuilder("- Profiles: ");
        line.Append(entries.Count == 0 ? "none set yet" : string.Join(", ", entries)).Append('.');
        var facts = new List<string>();
        if (langVersions.Count > 0)
        {
            facts.Add("C# " + string.Join(", ", langVersions.Order(LangVersionComparer.Instance)));
        }

        if (frameworks.Count > 0)
        {
            facts.Add("targets " + string.Join(", ", frameworks.Order(StringComparer.OrdinalIgnoreCase)));
        }

        if (facts.Count > 0)
        {
            line.Append(' ').Append(string.Join("; ", facts)).Append(". Do not use newer syntax or APIs.");
        }

        return line.ToString();
    }

    /// <summary>
    /// <c>dir/** = profile</c> when every project at or below <paramref name="directory"/> has that profile;
    /// otherwise the projects directly in it one by one, then each subdirectory the same way. Projects without a
    /// known profile are never named, but keep their directory from collapsing.
    /// </summary>
    private static void Collapse(string directory, IReadOnlyList<Project> projects, List<string> entries)
    {
        string[] profiles = projects.Select(p => p.Profile ?? string.Empty).Distinct(StringComparer.Ordinal).ToArray();
        if (profiles is [{ Length: > 0 } profile])
        {
            if (directory.Length == 0)
            {
                // Every project agrees: the narrowest directory that holds them all, src/** rather than **.
                while (!projects.Any(p => DirectoryOf(p.RelativePath) == directory) &&
                    projects.Select(p => ChildOf(directory, p.RelativePath)).Distinct(StringComparer.Ordinal).ToArray() is [string only])
                {
                    directory = only;
                }
            }

            entries.Add((directory.Length == 0 ? "**" : directory + "/**") + " = " + profile);
            return;
        }

        foreach (Project project in projects.Where(p => DirectoryOf(p.RelativePath) == directory && p.Profile is not null))
        {
            entries.Add(project.RelativePath + " = " + project.Profile);
        }

        IEnumerable<IGrouping<string, Project>> children = projects
            .Where(p => DirectoryOf(p.RelativePath) != directory)
            .GroupBy(p => ChildOf(directory, p.RelativePath), StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal);
        foreach (IGrouping<string, Project> child in children)
        {
            Collapse(child.Key, child.ToArray(), entries);
        }
    }

    /// <summary>The subdirectory of <paramref name="directory"/> that holds <paramref name="relativePath"/>.</summary>
    private static string ChildOf(string directory, string relativePath)
    {
        int start = directory.Length == 0 ? 0 : directory.Length + 1;
        return relativePath[..relativePath.IndexOf('/', start)];
    }

    private static string DirectoryOf(string relativePath)
    {
        int slash = relativePath.LastIndexOf('/');
        return slash < 0 ? string.Empty : relativePath[..slash];
    }

    /// <summary>The directories from the file's own up to the root (<c>""</c>).</summary>
    private static IEnumerable<string> Ancestors(string relativePath)
    {
        for (string directory = DirectoryOf(relativePath); ; directory = DirectoryOf(directory))
        {
            yield return directory;
            if (directory.Length == 0)
            {
                yield break;
            }
        }
    }

    private static bool IsProjectFile(string path) => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase);

    private static bool IsBuildProps(string path) =>
        Path.GetFileName(path).Equals("Directory.Build.props", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A PackageReference in the project or a Directory.Build.props. Directory.Packages.props only pins versions, so
    /// it is never read.
    /// </summary>
    private static bool MentionsPackage(string path)
    {
        try
        {
            return File.ReadAllText(path).Contains(ProjectInfoProvider.PackageId, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsSdkStyle(string path)
    {
        try
        {
            return SdkStyleDetector.IsSdkStyle(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary><c>9</c> becomes <c>9.0</c>; symbolic values such as <c>latest</c> are kept as written.</summary>
    private static string? LangVersion(string? value)
    {
        string trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return null;
        }

        return trimmed.All(char.IsAsciiDigit) ? trimmed + ".0" : trimmed;
    }

    /// <summary>Runs in the repository, so its global.json selects the SDK.</summary>
    private string? DotnetVersion(string root)
    {
        ToolResult result = _runner
            .RunAsync("dotnet", ["--version"], root, VersionTimeout, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        return result.Succeeded ? result.FirstLines(1).FirstOrDefault()?.Trim() : null;
    }

    private sealed class Project(string relativePath, bool sdkStyle, bool onboarded)
    {
        /// <summary>Relative to the root, with forward slashes.</summary>
        public string RelativePath { get; } = relativePath;

        public bool SdkStyle { get; } = sdkStyle;

        public bool Onboarded { get; } = onboarded;

        /// <summary>A valid profile from the project's marker, or <see langword="null"/>.</summary>
        public string? Profile { get; set; }
    }

    /// <summary>Numeric versions in numeric order, then symbolic ones.</summary>
    private sealed class LangVersionComparer : IComparer<string>
    {
        public static LangVersionComparer Instance { get; } = new();

        public int Compare(string? x, string? y)
        {
            bool xNumeric = Version.TryParse(x, out Version? xVersion);
            bool yNumeric = Version.TryParse(y, out Version? yVersion);
            return (xNumeric, yNumeric) switch
            {
                (true, true) => xVersion!.CompareTo(yVersion),
                (true, false) => -1,
                (false, true) => 1,
                _ => StringComparer.OrdinalIgnoreCase.Compare(x, y),
            };
        }
    }
}
