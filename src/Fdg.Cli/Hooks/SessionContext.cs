using System.ComponentModel;
using System.Text;
using Fdg.Cli.Commands;
using Fdg.Cli.Projects;
using Fdg.Cli.Tooling;

namespace Fdg.Cli.Hooks;

/// <summary>
/// What SessionStart tells the AI: the context lines, then one line per setup issue. The context is empty when only
/// legacy projects use the analyzers.
/// </summary>
public sealed record SessionReport(string Context, IReadOnlyList<string> Issues)
{
    public string Text => string.Join("\n", Issues.Prepend(Context).Where(l => l.Length > 0));
}

/// <summary>
/// The SessionStart context (design §5.5) and self-check. Facts come from the files only: project files, the
/// Directory.Build.props above them and the analyzer package's <c>fdg/marker*.json</c> in the intermediate
/// directory; MSBuild is never run.
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

    private const string BuildProps = "Directory.Build.props";

    private static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(30);

    private static readonly string[] Profiles = ["library", "application", "test"];

    private static readonly string[] Pathspecs = [":(glob,icase)**/*.csproj", ":(glob,icase)**/" + BuildProps];

    private readonly IProcessRunner _runner;

    /// <param name="runner">Runs <c>dotnet --version</c>.</param>
    public SessionContext(IProcessRunner runner)
    {
        _runner = runner;
    }

    /// <param name="repoRoot">
    /// A directory in the repository, whose git top level is used; outside git, the session's directory.
    /// </param>
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
        if (!projects.Any(p => p.Onboarded))
        {
            return null;
        }

        // Parsed only now: most sessions are in repositories that do not use the analyzers.
        foreach (Project project in projects)
        {
            project.SdkStyle = IsSdkStyle(Path.Combine(root, project.RelativePath));
        }

        var noProfile = new List<string>();
        var neverBuilt = new List<string>();
        var newerProtocol = new List<string>();
        var langVersions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var frameworks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var onboarded = projects.Where(p => p.SdkStyle && p.Onboarded).ToList();
        foreach (Project project in onboarded)
        {
            IReadOnlyList<ProjectMarker> markers = CurrentMarkers(root, project);
            if (markers.Count == 0)
            {
                neverBuilt.Add(project.RelativePath);
                continue;
            }

            // marker.json comes first and holds the profile; a per-framework one stands in only without it.
            string? profile = markers[0].Profile?.Trim().ToLowerInvariant();
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
        if (onboarded.Count > 0)
        {
            context.Append(Header).Append('\n');
            context.Append(ProfilesLine(projects, langVersions, frameworks)).Append('\n');
            context.Append(StyleLine).Append('\n');
            context.Append(AutofixLine).Append('\n');
            context.Append(SkillsLine);
        }

        var facts = new SetupFacts(
            DotnetVersion(root),
            noProfile,
            neverBuilt,
            newerProtocol,
            projects.Count(p => !p.SdkStyle),
            git is not null);
        return new SessionReport(context.ToString(), SelfCheck.Issues(facts));
    }

    /// <summary>
    /// The project's markers, <c>marker.json</c> first, from <c>obj/fdg/</c> or else the artifacts layout
    /// (<c>UseArtifactsOutput</c>: <c>&lt;Directory.Build.props directory&gt;/artifacts/obj/&lt;project name&gt;/fdg/</c>).
    /// </summary>
    private static IReadOnlyList<ProjectMarker> CurrentMarkers(string root, Project project)
    {
        string fullPath = Path.Combine(root, project.RelativePath);
        string name = Path.GetFileNameWithoutExtension(fullPath);
        IEnumerable<string> artifactRoots = new[] { project.BuildPropsDirectory, string.Empty }
            .OfType<string>()
            .Distinct(ProjectFiles.PathComparer)
            .Select(d => Path.Combine(root, d, "artifacts", "obj", name));
        foreach (string directory in artifactRoots.Prepend(Path.Combine(Path.GetDirectoryName(fullPath)!, "obj")))
        {
            IReadOnlyList<ProjectMarker> markers = CurrentMarkers(Path.Combine(directory, "fdg"), Path.GetFileName(fullPath));
            if (markers.Count > 0)
            {
                return markers;
            }
        }

        return [];
    }

    /// <summary>
    /// The package writes markers but never deletes them. A project that targets one framework now, whose
    /// <c>marker.json</c> framework has no <c>marker.&lt;tfm&gt;.json</c>, keeps only <c>marker.json</c>; the others are
    /// left over from when it targeted several. A marker written by another project in the same directory is skipped.
    /// </summary>
    private static IReadOnlyList<ProjectMarker> CurrentMarkers(string markerDirectory, string projectFileName)
    {
        if (!Directory.Exists(markerDirectory))
        {
            return [];
        }

        // By file name, so markers stay valid when the repository moves.
        bool Own(ProjectMarker marker) =>
            string.IsNullOrEmpty(marker.ProjectPath) ||
            ProjectFiles.PathComparer.Equals(Path.GetFileName(marker.ProjectPath.Replace('\\', '/')), projectFileName);

        ProjectMarker? main = ProjectMarker.Read(Path.Combine(markerDirectory, "marker.json")) is { } read && Own(read) ? read : null;
        ProjectMarker[] perFramework = Directory.EnumerateFiles(markerDirectory, "marker.*.json")
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(ProjectMarker.Read)
            .OfType<ProjectMarker>()
            .Where(Own)
            .ToArray();
        if (main is null)
        {
            return perFramework;
        }

        bool multiTargeting = perFramework.Any(m =>
            string.Equals(m.TargetFramework?.Trim(), main.TargetFramework?.Trim(), StringComparison.OrdinalIgnoreCase));
        return multiTargeting ? [main, .. perFramework] : [main];
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
        return Directory.EnumerateFiles(root, "*.csproj", options)
            .Concat(Directory.EnumerateFiles(root, BuildProps, options))
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .ToArray();
    }

    /// <summary>The projects and whether they use the analyzers; whether they are SDK-style is left for later.</summary>
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

            string[] ancestors = Ancestors(relativePath).ToArray();
            bool onboarded = MentionsPackage(fullPath) || ancestors.Any(d => buildProps.GetValueOrDefault(d));
            projects.Add(new Project(relativePath, onboarded, ancestors.FirstOrDefault(buildProps.ContainsKey)));
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

    private static bool IsBuildProps(string path) => Path.GetFileName(path).Equals(BuildProps, StringComparison.OrdinalIgnoreCase);

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
    /// <returns><see langword="null"/> when it fails or <c>dotnet</c> cannot be started.</returns>
    private string? DotnetVersion(string root)
    {
        ToolResult result;
        try
        {
            result = _runner
                .RunAsync("dotnet", ["--version"], root, VersionTimeout, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }
        catch (Win32Exception)
        {
            return null;
        }

        return result.Succeeded ? result.FirstLines(1).FirstOrDefault()?.Trim() : null;
    }

    private sealed class Project(string relativePath, bool onboarded, string? buildPropsDirectory)
    {
        /// <summary>Relative to the root, with forward slashes.</summary>
        public string RelativePath { get; } = relativePath;

        public bool Onboarded { get; } = onboarded;

        /// <summary>The nearest directory at or above the project's with a Directory.Build.props, if any.</summary>
        public string? BuildPropsDirectory { get; } = buildPropsDirectory;

        /// <summary>Set once some project is onboarded.</summary>
        public bool SdkStyle { get; set; }

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
