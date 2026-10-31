using System.ComponentModel;
using System.Text;
using Fdg.Cli.Commands;
using Fdg.Cli.Projects;
using Fdg.Cli.Tooling;

namespace Fdg.Cli.Hooks;

/// <summary>
/// What SessionStart tells the AI: the context lines, then one line per setup issue. The context is empty when only
/// legacy projects reference the analyzers.
/// </summary>
public sealed record SessionReport(string Context, IReadOnlyList<string> Issues)
{
    public string Text => string.Join("\n", Issues.Prepend(Context).Where(l => l.Length > 0));
}

/// <summary>
/// The SessionStart context (design §5.5) and self-check. Facts come from the files only: project files, the
/// files <see cref="Onboarding"/> reads and the analyzer package's <c>fdg/marker*.json</c> in the intermediate
/// directory; MSBuild is never run. Only <c>dotnet --version</c> runs, and the SDK's compiler when a marker has a
/// symbolic language version.
/// </summary>
public sealed class SessionContext
{
    private const string Header = "This repository follows Framework Design Guidelines (3rd ed.).";

    private const string StyleLine =
        "- Write in .NET BCL style: Allman braces, one statement per line, parameter/argument lists that are wrapped or " +
        "exceed the line limit chopped before the first item, one per line.";

    private const string AutofixLine =
        "- Layout and machine-checkable rules are auto-fixed and checked at the end of each turn; only fix the FDG/CA/IDE " +
        "issues reported to you.";

    private const string SkillsLine =
        "- Before designing or changing types, members, exception handling or common patterns, read the fdg-write " +
        "skill. Use fdg-review to review an API.";

    private const string BuildProps = "Directory.Build.props";

    // Well within the hosts' 15-second SessionStart timeout, which would drop the whole context.
    private static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(5);

    private static readonly string[] Profiles = ["library", "application", "test"];

    private static readonly string[] Pathspecs = [":(glob,icase)**/*.csproj", ":(glob,icase)**/" + BuildProps];

    private readonly IProcessRunner _runner;
    private readonly ILanguageVersionSource _languageVersions;

    /// <param name="runner">Runs <c>dotnet --version</c> and the compiler that resolves a symbolic language version.</param>
    public SessionContext(IProcessRunner runner)
        : this(runner, new CompilerLanguageVersions(runner))
    {
    }

    /// <param name="runner">Runs <c>dotnet --version</c>.</param>
    /// <param name="languageVersions">Resolves a symbolic language version in a marker, such as <c>latest</c>.</param>
    public SessionContext(IProcessRunner runner, ILanguageVersionSource languageVersions)
    {
        _runner = runner;
        _languageVersions = languageVersions;
    }

    /// <summary>The issue line added when <see cref="Inspect"/> runs out of time.</summary>
    public const string TimedOutLine = "FDG: session inspection timed out; some checks were skipped.";

    /// <param name="repoRoot">
    /// A directory in the repository, whose git top level is used; outside git, the session's directory.
    /// </param>
    /// <returns>The text to add to the AI's context, or <see langword="null"/> when no project uses the analyzers.</returns>
    public string? Build(string repoRoot) => Inspect(repoRoot, CancellationToken.None)?.Text;

    /// <inheritdoc cref="Build"/>
    /// <param name="cancellationToken">
    /// Ends the inspection early, killing a process it runs: what was found by then is reported, followed by
    /// <see cref="TimedOutLine"/>. When it ends before any project is known to use the analyzers, the report has no
    /// context and <see cref="TimedOutLine"/> only, since the repository may use them.
    /// </param>
    public SessionReport? Inspect(string repoRoot, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(repoRoot))
        {
            return null;
        }

        GitRepository? git;
        IReadOnlyList<string> files;
        try
        {
            git = GitRepository.OpenAsync(repoRoot, cancellationToken).GetAwaiter().GetResult();
            files = git is null
                ? ListNearbyFiles(Path.GetFullPath(repoRoot))
                : git.ListFilesAsync(Pathspecs, cancellationToken).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return TimedOutBeforeAnyProject();
        }

        string root = git?.TopLevel ?? Path.GetFullPath(repoRoot);
        List<Project> projects = Discover(root, files, cancellationToken, out bool complete);
        if (!projects.Any(p => p.Onboarded))
        {
            return complete ? null : TimedOutBeforeAnyProject();
        }

        // Parsed only now: most sessions are in repositories that do not use the analyzers.
        foreach (Project project in projects)
        {
            project.SdkStyle = IsSdkStyle(Path.Combine(root, project.RelativePath));
        }

        bool timedOut = cancellationToken.IsCancellationRequested;
        var noProfile = new List<string>();
        var neverBuilt = new List<string>();
        var newerProtocol = new List<string>();
        var langVersions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolvedLangVersions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var frameworks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var onboarded = projects.Where(p => p.SdkStyle && p.Onboarded).ToList();
        foreach (Project project in onboarded)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                // The rest are left out rather than reported as never built.
                timedOut = true;
                break;
            }

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

            string projectDirectory = Path.GetDirectoryName(Path.GetFullPath(Path.Combine(root, project.RelativePath)))!;
            foreach (ProjectMarker marker in markers)
            {
                if (LangVersion(marker.LangVersion) is string langVersion)
                {
                    if (SupportedLanguageVersions.IsSymbolic(langVersion))
                    {
                        try
                        {
                            langVersion = ResolveLangVersion(projectDirectory, langVersion, resolvedLangVersions, cancellationToken);
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            timedOut = true;
                            langVersion = Unresolved(langVersion);
                        }
                    }

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
            int otherLegacy = projects.Count(p => !p.SdkStyle && !p.Onboarded);
            if (otherLegacy > 0)
            {
                // A fact for the AI only: nothing to fix (Ruling 51).
                context.Append('\n').Append("- Legacy projects (not checked): ").Append(otherLegacy);
            }
        }

        string? dotnetVersion = null;
        bool dotnetChecked = false;
        if (!timedOut)
        {
            try
            {
                dotnetVersion = DotnetVersion(root, cancellationToken);
                dotnetChecked = true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                timedOut = true;
            }
        }

        var facts = new SetupFacts(
            dotnetVersion,
            noProfile,
            neverBuilt,
            newerProtocol,
            projects.Where(p => !p.SdkStyle && p.Onboarded).Select(p => p.RelativePath).ToList(),
            git is not null,
            dotnetChecked);
        IReadOnlyList<string> issues = SelfCheck.Issues(facts);
        return new SessionReport(context.ToString(), timedOut ? [.. issues, TimedOutLine] : issues);
    }

    /// <summary>
    /// Out of time before any project was known to use the analyzers: no context, which would be guesswork, but the
    /// user learns that the checks did not run.
    /// </summary>
    private static SessionReport TimedOutBeforeAnyProject() => new(string.Empty, [TimedOutLine]);

    /// <summary>
    /// The project's markers, <c>marker.json</c> first, from <c>obj/fdg/</c> or the artifacts layout
    /// (<c>UseArtifactsOutput</c>: <c>&lt;Directory.Build.props directory&gt;/artifacts/obj/&lt;project name&gt;/fdg/</c>),
    /// whichever was written last.
    /// </summary>
    private static IReadOnlyList<ProjectMarker> CurrentMarkers(string root, Project project)
    {
        string fullPath = Path.Combine(root, project.RelativePath);
        string name = Path.GetFileNameWithoutExtension(fullPath);
        IEnumerable<string> artifactRoots = new[] { project.BuildPropsDirectory, string.Empty }
            .OfType<string>()
            .Select(d => Path.Combine(root, d, "artifacts", "obj", name));
        IEnumerable<string> directories = artifactRoots
            .Prepend(Path.Combine(Path.GetDirectoryName(fullPath)!, "obj"))
            .Select(d => Path.Combine(d, "fdg"));
        return ProjectMarker.ReadCurrent(directories, fullPath);
    }

    /// <summary>
    /// What the compiler makes of a symbolic language version, asked once per value; for example
    /// <c>latest (unresolved)</c> when it cannot tell.
    /// </summary>
    private string ResolveLangVersion(
        string projectDirectory,
        string symbolic,
        Dictionary<string, string> resolved,
        CancellationToken cancellationToken)
    {
        if (!resolved.TryGetValue(symbolic, out string? langVersion))
        {
            langVersion = _languageVersions.Resolve(projectDirectory, symbolic, cancellationToken) ?? Unresolved(symbolic);
            resolved.Add(symbolic, langVersion);
        }

        return langVersion;
    }

    private static string Unresolved(string symbolic) => symbolic + " (unresolved)";

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
    /// <param name="cancellationToken">Ends the discovery early, with the projects found by then.</param>
    /// <param name="complete">Set to <see langword="false"/> when <paramref name="cancellationToken"/> ended it early.</param>
    private static List<Project> Discover(
        string root,
        IReadOnlyList<string> files,
        CancellationToken cancellationToken,
        out bool complete)
    {
        complete = true;
        // bin/, obj/ and dot-directories hold no projects of the repository's own.
        string[] inScope = files
            .Where(f => !f.Split('/')[..^1].Any(ProjectFiles.IsSkippedDirectory))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var buildProps = inScope.Where(IsBuildProps).Select(DirectoryOf).ToHashSet(ProjectFiles.PathComparer);
        var onboarding = new Onboarding(root);
        var projects = new List<Project>();
        foreach (string relativePath in inScope.Where(IsProjectFile))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                complete = false;
                break;
            }

            string fullPath = Path.Combine(root, relativePath);
            if (!File.Exists(fullPath))
            {
                // Tracked but deleted in the working tree.
                continue;
            }

            projects.Add(new Project(
                relativePath,
                onboarding.IsOnboarded(Path.GetFullPath(fullPath)),
                Ancestors(relativePath).FirstOrDefault(buildProps.Contains)));
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

    /// <summary><c>9</c> becomes <c>9.0</c>; symbolic values such as <c>latest</c> are kept as written, to be resolved.</summary>
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
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    private string? DotnetVersion(string root, CancellationToken cancellationToken)
    {
        ToolResult result;
        try
        {
            result = _runner
                .RunAsync("dotnet", ["--version"], root, VersionTimeout, cancellationToken)
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
