using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Fdg.Cli.Commands;
using Fdg.Cli.IO;
using Fdg.Cli.Projects;
using Fdg.Cli.Rules;
using Fdg.Cli.Tooling;
using Microsoft.CodeAnalysis;

namespace Fdg.Cli.Hooks;

/// <summary>
/// The hook logic. Every method returns the hook's stdout: <see cref="HookOutput.None"/> or one JSON object.
/// Nothing is written outside the state directory except the files dotnet format changes.
/// </summary>
public sealed partial class HookRunner
{
    // Each dotnet format loads a whole workspace into memory, so only a few run at once.
    private const int MaxParallelFormats = 4;

    private static readonly TimeSpan CleanupGrace = TimeSpan.FromSeconds(2);

    // The hosts give SessionStart 15 seconds and drop its whole output after that; the rest of it, process start
    // included, takes well under a second.
    private static readonly TimeSpan SessionInspectionTimeout = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan StaleSessionAge = TimeSpan.FromDays(7);

    private readonly IProjectInfoProvider _projects;
    private readonly IFormatRunner _formatter;
    private readonly IBuildRunner _builder;
    private readonly SessionContext _session;
    private readonly Func<string, string?> _environment;

    public HookRunner(
        IProjectInfoProvider projects,
        IFormatRunner formatter,
        IBuildRunner builder,
        SessionContext session,
        Func<string, string?> environment)
    {
        _projects = projects;
        _formatter = formatter;
        _builder = builder;
        _session = session;
        _environment = environment;
    }

    /// <summary>
    /// SessionStart: adds the FDG context and the self-check's setup issues to the AI's context, and shows the issues
    /// to the user. Outside a git repository the session's directory stands in for the repository root. In an
    /// onboarded repository, also removes the state other sessions left more than <see cref="StaleSessionAge"/> ago.
    /// </summary>
    public async Task<string> SessionStartAsync(HookInput input, CancellationToken cancellationToken)
    {
        // Inspect finds the repository root itself. Out of time, it kills what it runs and reports what it found.
        SessionReport? report;
        using (var inspection = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            inspection.CancelAfter(SessionInspectionTimeout);
            report = _session.Inspect(input.Cwd, inspection.Token);
        }

        if (report is null)
        {
            return HookOutput.None;
        }

        if (await GitRepository.OpenAsync(input.Cwd, cancellationToken).ConfigureAwait(false) is GitRepository git)
        {
            StateStore.DeleteOtherSessions(git, input.SessionId, DateTime.UtcNow - StaleSessionAge);
        }

        return HookOutput.Context(
            input.EventName,
            report.Text,
            report.Issues.Count == 0 ? null : string.Join("\n", report.Issues));
    }

    /// <summary>
    /// UserPromptSubmit: snapshots the working tree and resets the block count. On Codex, which does not notice
    /// files changed behind its back, also tells the AI what the last Stop hook reformatted. Does nothing in a
    /// repository without onboarded projects.
    /// </summary>
    public async Task<string> TurnStartAsync(HookInput input, CancellationToken cancellationToken)
    {
        GitRepository? git = await GitRepository.OpenAsync(input.Cwd, cancellationToken).ConfigureAwait(false);
        if (git is null || !await Onboarding.IsRepositoryOnboardedAsync(git, cancellationToken).ConfigureAwait(false))
        {
            return HookOutput.None;
        }

        StateStore state = StateStore.For(git, input.SessionId);
        AutofixRecord? autofix = state.ReadAutofix();
        state.WriteAutofix(null);
        await Snapshot.TakeAsync(git, state, cancellationToken).ConfigureAwait(false);
        return autofix is not null && autofix.Files.Count > 0 && input.Host == HostKind.Codex
            ? HookOutput.Context(input.EventName, autofix.Summary())
            : HookOutput.None;
    }

    /// <summary>
    /// Stop: formats, builds and reports the turn's changes (design §5.4), within the configured timeout. Does
    /// nothing in a repository without onboarded projects.
    /// </summary>
    public async Task<string> TurnStopAsync(HookInput input, CancellationToken cancellationToken)
    {
        GitRepository? git = await GitRepository.OpenAsync(input.Cwd, cancellationToken).ConfigureAwait(false);
        if (git is null || !await Onboarding.IsRepositoryOnboardedAsync(git, cancellationToken).ConfigureAwait(false))
        {
            return HookOutput.None;
        }

        StateStore state = StateStore.For(git, input.SessionId);
        TurnSnapshot? snapshot = state.ReadSnapshot();
        if (snapshot is null)
        {
            // No state at all: the repository was not onboarded when the turn started, as in the turn that set it up.
            if (!Directory.Exists(state.Directory))
            {
                return HookOutput.None;
            }

            return HookOutput.SystemMessage("FDG: no snapshot from the start of this turn; the Stop check was skipped.");
        }

        HookSettings settings = HookSettings.FromEnvironment(_environment);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(settings.Timeout);
        Task<string> work = Task.Run(() => StopAsync(input, git, state, snapshot, settings, timeout.Token), CancellationToken.None);
        try
        {
            // WaitAsync also bounds the synchronous parts, such as MSBuild property queries.
            return await work.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // Give the cancelled process runs a moment to kill their process trees before the CLI exits.
            await Task.WhenAny(work, Task.Delay(CleanupGrace, CancellationToken.None)).ConfigureAwait(false);
            return HookOutput.SystemMessage(
                $"FDG: the Stop check timed out after {settings.Timeout.TotalSeconds:0} s (FDG_HOOK_TIMEOUT_SECONDS); " +
                "this turn was not checked, and files may have been partially reformatted.");
        }
    }

    private async Task<string> StopAsync(
        HookInput input,
        GitRepository git,
        StateStore state,
        TurnSnapshot snapshot,
        HookSettings settings,
        CancellationToken cancellationToken)
    {
        // 1. The turn's changed files, in scope only, grouped by project.
        TurnChanges changes = await Snapshot.FindChangesAsync(git, snapshot, cancellationToken).ConfigureAwait(false);
        var notes = new List<string>(changes.Notes);
        IReadOnlyList<ProjectWork> projects = GroupByProject(git, changes.Changed, notes);
        if (projects.Count == 0)
        {
            // Nothing in scope is left changed (the changes were reverted), so there is nothing to re-read.
            state.WriteAutofix(null);
            state.WriteAiLines(new Dictionary<string, int[]>());
            return Finish(notes, []);
        }

        foreach (string path in projects.SelectMany(p => p.Files))
        {
            string aiVersion = state.AiVersionPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(aiVersion)!);
            AtomicFile.Copy(Snapshot.FullPath(git, path), aiVersion);
        }

        // 2. Per project, a few at a time: format. One pass when building, since the build tells whether another is
        // needed (step 3); without a build nothing would.
        int formatPasses = settings.Build ? 1 : FormatPasses.MaxPasses;
        var formatNotes = new ConcurrentDictionary<int, string>();
        await Parallel.ForEachAsync(
            projects.Select((project, index) => (Project: project, Index: index)),
            new ParallelOptions { MaxDegreeOfParallelism = MaxParallelFormats, CancellationToken = cancellationToken },
            async (item, token) =>
            {
                if (await FormatProjectAsync(git, item.Project, formatPasses, token).ConfigureAwait(false) is string note)
                {
                    formatNotes[item.Index] = note;
                }
            }).ConfigureAwait(false);

        // 3-4. One project at a time, since builds of projects that reference each other would compile the shared
        // ones at once: build, format again if the build still finds something formatting fixes, read the SARIF logs.
        // A referenced project changed this turn, onboarded or not, is built with the project that uses it.
        string[] turnFiles = changes.Changed.Concat(changes.Deleted).Select(f => Snapshot.FullPath(git, f)).ToArray();
        var diagnostics = new List<(ProjectWork Project, SarifDiagnostic Diagnostic)>();
        var failedBuilds = new List<(ProjectWork Project, ToolResult Build, bool LogsRead)>();
        for (int i = 0; i < projects.Count; i++)
        {
            string? note = formatNotes.GetValueOrDefault(i);
            if (settings.Build)
            {
                (ToolResult build, bool hasLogs, string? buildNote, string? formatNote) = await BuildProjectAsync(
                    git, state, snapshot.Turn, projects[i], turnFiles, formatted: note is null, diagnostics, cancellationToken)
                    .ConfigureAwait(false);
                if (!build.Succeeded && hasLogs)
                {
                    failedBuilds.Add((projects[i], build, buildNote is null));
                }

                note ??= formatNote ?? buildNote;
            }

            if (note is not null)
            {
                notes.Add(note);
            }
        }

        // 6. What formatting changed, for the next turn (and for Codex when blocking now).
        AutofixRecord? autofix = await RecordAutofixAsync(git, state, snapshot.Turn, projects, cancellationToken).ConfigureAwait(false);

        // 4-5. Filter on the lines the AI changed since the turn started, then report.
        var moveSources = new MoveSources(git, state, snapshot, changes.Deleted);
        IReadOnlyDictionary<string, IReadOnlySet<int>> changedLines = await ComputeChangedLinesAsync(
            git, state, snapshot, moveSources, projects, cancellationToken).ConfigureAwait(false);

        var registered = new Dictionary<RulesManifest, IReadOnlySet<string>>(ReferenceEqualityComparer.Instance);
        foreach (RulesManifest manifest in projects.Select(p => p.Manifest))
        {
            registered.TryAdd(manifest, manifest.RegisteredDiagnostics.ToHashSet(StringComparer.Ordinal));
        }

        foreach ((ProjectWork project, ToolResult build, bool logsRead) in failedBuilds)
        {
            if (BuildFailure(git, project, build, logsRead, diagnostics, changedLines, registered[project.Manifest]) is string note &&
                !notes.Contains(note))
            {
                notes.Add(note);
            }
        }

        List<ReportedIssue> issues = await ToIssuesAsync(
            git, state, snapshot, moveSources, diagnostics, changedLines, registered, cancellationToken).ConfigureAwait(false);
        if (issues.Count == 0)
        {
            return Finish(notes, []);
        }

        // Issues on API that existed when the turn started are told to the user; the AI must not change that API.
        int toFix = issues.Count(i => !i.ExistingApi);
        if (toFix == 0)
        {
            return Finish(
                notes,
                [StopReport.Format(
                    issues,
                    $"FDG: {issues.Count} issue(s) on existing API in lines you changed; not blocking. Tell the user instead of changing the API.")]);
        }

        // stop_hook_active false means the host is not continuing after a block, so this is a new turn even if
        // UserPromptSubmit never reset the count. A payload without the field keeps the count, so the limit holds.
        int blocks = input.StopHookActive == false ? 0 : state.ReadBlocks();
        string count = $"FDG found {toFix} issue(s) in lines you changed this turn.";
        if (blocks < settings.MaxBlocks)
        {
            state.WriteBlocks(blocks + 1);
            string reason = StopReport.Format(issues, count + " Fix them before finishing.");
            if (input.Host == HostKind.Codex && autofix is not null)
            {
                reason += "FDG reformatted files; re-read them before editing: " +
                    string.Join(", ", autofix.Files.Select(f => f.Path)) + "\n";
            }

            return HookOutput.Block(reason + string.Concat(notes.Select(n => n + "\n")));
        }

        return Finish(
            notes,
            [StopReport.Format(issues, $"{count} Not blocking again this turn (FDG_HOOK_MAX_BLOCKS={settings.MaxBlocks}).")]);
    }

    /// <summary>
    /// The lines the AI changed this turn per full file path, in the formatted files. The first Stop of a turn
    /// compares with the baseline; a later one continues from the formatted copy and lines the previous Stop kept,
    /// so formatting done by an earlier Stop never counts as the AI's.
    /// </summary>
    private static async Task<IReadOnlyDictionary<string, IReadOnlySet<int>>> ComputeChangedLinesAsync(
        GitRepository git,
        StateStore state,
        TurnSnapshot snapshot,
        MoveSources moveSources,
        IReadOnlyList<ProjectWork> projects,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, int[]> previousAiLines = state.ReadAiLines();
        var aiLines = new Dictionary<string, int[]>(StringComparer.Ordinal);
        var changedLines = new Dictionary<string, IReadOnlySet<int>>(ProjectFiles.PathComparer);
        foreach (string path in projects.SelectMany(p => p.Files).Distinct(StringComparer.Ordinal))
        {
            string current = Snapshot.FullPath(git, path);
            string aiVersion = state.AiVersionPath(path);
            string previous = state.FormattedPath(path);
            IReadOnlySet<int> lines;
            if (previousAiLines.TryGetValue(path, out int[]? previousLines) && File.Exists(previous))
            {
                lines = await ChangedLines.ComputeSinceAsync(
                    git, previous, previousLines.ToHashSet(), aiVersion, current, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                string baseline = await Snapshot.WriteBaselineAsync(git, state, snapshot, path, moveSources, cancellationToken)
                    .ConfigureAwait(false);
                lines = await ChangedLines.ComputeAsync(git, baseline, aiVersion, current, cancellationToken).ConfigureAwait(false);
            }

            changedLines[current] = lines;
            aiLines[path] = lines.Order().ToArray();
            Directory.CreateDirectory(Path.GetDirectoryName(previous)!);
            AtomicFile.Copy(current, previous);
        }

        state.WriteAiLines(aiLines);
        return changedLines;
    }

    private static string Finish(IReadOnlyList<string> notes, IReadOnlyList<string> report)
    {
        string message = string.Join("\n", report.Select(r => r.TrimEnd('\n')).Concat(notes));
        return message.Length == 0 ? HookOutput.None : HookOutput.SystemMessage(message);
    }

    /// <summary>
    /// Assigns each changed file to the first in-scope project in its directory (two projects formatting one file
    /// at once would race). Files under no project, or under an out-of-scope or unrestored project, are skipped;
    /// only an onboarded project that cannot be checked says so.
    /// </summary>
    private List<ProjectWork> GroupByProject(GitRepository git, IReadOnlyList<string> changed, List<string> notes)
    {
        var onboarding = new Onboarding(git.TopLevel);
        var projects = new Dictionary<string, ProjectWork?>(ProjectFiles.PathComparer);
        var work = new List<ProjectWork>();
        foreach (string path in changed)
        {
            string directory = Path.GetDirectoryName(Snapshot.FullPath(git, path))!;
            foreach (string projectPath in ProjectFiles.FindOwningProjects(directory))
            {
                if (!projects.TryGetValue(projectPath, out ProjectWork? project))
                {
                    project = onboarding.IsOnboarded(projectPath) ? GetInScope(git, projectPath, notes) : null;
                    projects.Add(projectPath, project);
                }

                if (project is not null)
                {
                    if (project.Files.Count == 0)
                    {
                        work.Add(project);
                    }

                    project.Files.Add(path);
                    break;
                }
            }
        }

        return work;
    }

    private ProjectWork? GetInScope(GitRepository git, string projectPath, List<string> notes)
    {
        ProjectInfo info;
        try
        {
            info = _projects.Get(projectPath);
        }
        catch (ProjectEvaluationException e)
        {
            notes.Add($"FDG: cannot evaluate {Display(git, projectPath)}: {e.Message}; its files were not checked.");
            return null;
        }

        if (info.State == ProjectState.NotRestored)
        {
            notes.Add($"FDG: {Display(git, projectPath)} is not restored (run dotnet restore); its files were not checked.");
            return null;
        }

        if (info.State != ProjectState.InScope)
        {
            return null;
        }

        if (info.Protocol > FixCommand.SupportedProtocol)
        {
            notes.Add(
                $"FDG: {Display(git, projectPath)} uses package protocol {info.Protocol}, newer than this dotnet-fdg " +
                "supports; update it with: dotnet tool update -g dotnet-fdg");
            return null;
        }

        try
        {
            return new ProjectWork(info, RulesManifest.LoadOrEmbedded(info.RulesPath), []);
        }
        catch (NotSupportedException e)
        {
            notes.Add($"FDG: {Display(git, projectPath)}: {e.Message}; its files were not checked.");
            return null;
        }
    }

    /// <returns>A one-line note when formatting failed.</returns>
    private async Task<string?> FormatProjectAsync(GitRepository git, ProjectWork project, int maxPasses, CancellationToken cancellationToken)
    {
        string projectPath = project.Info.ProjectPath;
        string[] files = project.Files.Select(f => Snapshot.FullPath(git, f)).ToArray();
        ToolResult format = await FormatPasses.RunAsync(
            _formatter,
            projectPath,
            FormatIncludes.ToProjectRelative(projectPath, files),
            [],
            project.Manifest.FixDiagnostics,
            files,
            () => HashFiles(files),
            maxPasses,
            cancellationToken).ConfigureAwait(false);
        return format.Succeeded ? null : Failure(git, "dotnet format", projectPath, format);
    }

    /// <summary>
    /// Builds the project, without its dependencies when the turn changed none of them (<see cref="CanSkipDependencies"/>).
    /// When the build still finds a diagnostic that formatting fixes in a file of the project changed this turn, formats
    /// the project's files again with the passes left (<see cref="FormatPasses.MaxPasses"/> - 1) and, if that changed
    /// them, builds again and uses that build: one pass cannot fix nested occurrences, and what formatting cannot fix is
    /// reported from the build like anything else.
    /// </summary>
    /// <param name="formatted">Whether the first format succeeded; a failed one is not run again.</param>
    /// <returns>
    /// The build result, whether it wrote SARIF logs, a one-line note when the project's diagnostics could not be read,
    /// and a one-line note when the second format failed.
    /// </returns>
    private async Task<(ToolResult Build, bool HasLogs, string? Note, string? FormatNote)> BuildProjectAsync(
        GitRepository git,
        StateStore state,
        int turn,
        ProjectWork project,
        IReadOnlyList<string> turnFiles,
        bool formatted,
        List<(ProjectWork, SarifDiagnostic)> diagnostics,
        CancellationToken cancellationToken)
    {
        string projectPath = project.Info.ProjectPath;
        string sarifDirectory = state.SarifDirectory(turn, projectPath);
        bool canWriteLog = DotnetBuildRunner.CanWriteLog(projectPath, sarifDirectory);
        ProjectBuild result = await BuildWithFallbackAsync(
            projectPath, sarifDirectory, CanSkipDependencies(projectPath, turnFiles), canWriteLog, cancellationToken).ConfigureAwait(false);

        string? formatNote = null;
        if (formatted && HasFixableDiagnostic(git, project, result.Diagnostics))
        {
            string[] files = project.Files.Select(f => Snapshot.FullPath(git, f)).ToArray();
            string before = HashFiles(files);
            formatNote = await FormatProjectAsync(git, project, FormatPasses.MaxPasses - 1, cancellationToken).ConfigureAwait(false);

            // Unchanged files compile to the same diagnostics, so the logs already read still hold.
            if (HashFiles(files) != before)
            {
                result = await BuildWithFallbackAsync(projectPath, sarifDirectory, result.NoDependencies, canWriteLog, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        (ToolResult build, IReadOnlyList<string> logs, IReadOnlyList<SarifDiagnostic> found, bool unreadable, _) = result;
        if (logs.Count == 0)
        {
            if (build.Succeeded && !canWriteLog)
            {
                return (build, false, "FDG: the path of the git directory or worktree has a comma, so dotnet build cannot " +
                    $"write a SARIF log for {Display(git, projectPath)}; its diagnostics were not checked.", formatNote);
            }

            return (build, false, build.Succeeded
                ? $"FDG: dotnet build wrote no SARIF log for {Display(git, projectPath)}; its diagnostics were not checked."
                : Failure(git, "dotnet build", projectPath, build), formatNote);
        }

        diagnostics.AddRange(found.Select(d => (project, d)));
        return (build, true, unreadable
            ? $"FDG: cannot read the SARIF log of {Display(git, projectPath)}; its diagnostics were not checked."
            : null, formatNote);
    }

    /// <summary>
    /// Whether the project can be built against the existing output of the projects it references: their closure is
    /// known (<see cref="ProjectReferences.Closure"/>) and no file the turn changed or deleted, in scope or not, is
    /// under one of their directories.
    /// </summary>
    private static bool CanSkipDependencies(string projectPath, IReadOnlyList<string> turnFiles)
    {
        IReadOnlySet<string>? closure = ProjectReferences.Closure(projectPath);
        if (closure is null)
        {
            return false;
        }

        string[] directories = closure.Select(p => Path.GetDirectoryName(p)!).Distinct(ProjectFiles.PathComparer).ToArray();
        return !turnFiles.Any(file => directories.Any(directory => IsUnder(file, directory)));
    }

    private static bool IsUnder(string path, string directory)
    {
        string relative = Path.GetRelativePath(directory, path);
        return !Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    /// <summary>
    /// A warning or error that formatting fixes (<see cref="RulesManifest.FixDiagnostics"/>; dotnet format runs with
    /// <c>--severity warn</c>), not suppressed, on any line of a file of the project changed this turn.
    /// </summary>
    private static bool HasFixableDiagnostic(GitRepository git, ProjectWork project, IReadOnlyList<SarifDiagnostic> diagnostics)
    {
        var fixable = project.Manifest.FixDiagnostics.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var files = project.Files.Select(f => Snapshot.FullPath(git, f)).ToHashSet(ProjectFiles.PathComparer);
        return diagnostics.Any(d =>
            !d.Suppressed && (d.Level is "warning" or "error") && fixable.Contains(d.RuleId) && files.Contains(d.FilePath));
    }

    /// <summary>
    /// Builds as <see cref="BuildWithLogsAsync"/> does. A build without dependencies that wrote no log, or failed other
    /// than in copying its output after the compile, is done once more with them: the output of a referenced project
    /// may be missing, or out of date after a change made before the turn.
    /// </summary>
    private async Task<ProjectBuild> BuildWithFallbackAsync(
        string projectPath,
        string sarifDirectory,
        bool noDependencies,
        bool canWriteLog,
        CancellationToken cancellationToken)
    {
        ProjectBuild result = await BuildWithLogsAsync(projectPath, sarifDirectory, noDependencies, canWriteLog, cancellationToken)
            .ConfigureAwait(false);
        bool compiled = result.Logs.Count > 0 && (result.Build.Succeeded || IsCopyFailure(result.Build));
        bool expectsLog = canWriteLog || !result.Build.Succeeded;
        if (noDependencies && !compiled && expectsLog)
        {
            result = await BuildWithLogsAsync(projectPath, sarifDirectory, noDependencies: false, canWriteLog, cancellationToken)
                .ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>
    /// Builds and reads the logs. A build that succeeded without a log of the project's own is done again with
    /// <c>--no-incremental</c>.
    /// </summary>
    private async Task<ProjectBuild> BuildWithLogsAsync(
        string projectPath,
        string sarifDirectory,
        bool noDependencies,
        bool canWriteLog,
        CancellationToken cancellationToken)
    {
        (ToolResult build, IReadOnlyList<string> logs) = await BuildAsync(
            projectPath, sarifDirectory, noIncremental: false, noDependencies, cancellationToken).ConfigureAwait(false);
        (IReadOnlyList<SarifDiagnostic> found, bool unreadable) = ReadLogs(logs, projectPath);
        if (build.Succeeded && canWriteLog && (logs.Count == 0 || IsForeign(found, projectPath)))
        {
            // Up to date: CoreCompile was skipped and wrote no log, or only a referenced project wrote one (a
            // package without FdgSarifProject support). That is not "no issues".
            (build, logs) = await BuildAsync(projectPath, sarifDirectory, noIncremental: true, noDependencies, cancellationToken)
                .ConfigureAwait(false);
            (found, unreadable) = ReadLogs(logs, projectPath);
        }

        return new ProjectBuild(build, logs, found, unreadable, noDependencies);
    }

    private static (IReadOnlyList<SarifDiagnostic> Diagnostics, bool Unreadable) ReadLogs(IReadOnlyList<string> logs, string projectPath)
    {
        var diagnostics = new List<SarifDiagnostic>();
        bool unreadable = false;
        foreach (string log in logs)
        {
            try
            {
                diagnostics.AddRange(SarifReader.Read(log, Path.GetDirectoryName(projectPath)!));
            }
            catch (JsonException)
            {
                unreadable = true;
            }
        }

        return (diagnostics, unreadable);
    }

    /// <summary>
    /// A log with results, none of them in a file of the project, is a referenced project's. The project's files are
    /// those under its directory and those its project file lists in static <c>Compile</c> items, such as linked
    /// shared files (<see cref="ProjectFiles.StaticCompileItems"/>).
    /// </summary>
    private static bool IsForeign(IReadOnlyList<SarifDiagnostic> diagnostics, string projectPath)
    {
        var owners = new Dictionary<string, bool>(ProjectFiles.PathComparer);
        HashSet<string>? compileItems = null;
        return diagnostics.Count > 0 && !diagnostics.Any(d =>
        {
            string directory = Path.GetDirectoryName(d.FilePath) ?? string.Empty;
            if (!owners.TryGetValue(directory, out bool owned))
            {
                owned = Directory.Exists(directory) &&
                    ProjectFiles.FindOwningProjects(directory).Contains(projectPath, ProjectFiles.PathComparer);
                owners.Add(directory, owned);
            }

            if (owned)
            {
                return true;
            }

            compileItems ??= ProjectFiles.StaticCompileItems(projectPath).ToHashSet(ProjectFiles.PathComparer);
            return compileItems.Contains(Path.GetFullPath(d.FilePath));
        });
    }

    /// <returns>The build result and the SARIF logs it wrote, one per target framework.</returns>
    private async Task<(ToolResult Result, IReadOnlyList<string> Logs)> BuildAsync(
        string projectPath,
        string sarifDirectory,
        bool noIncremental,
        bool noDependencies,
        CancellationToken cancellationToken)
    {
        // Logs of an earlier Stop in this turn must not pass for this build's.
        if (Directory.Exists(sarifDirectory))
        {
            Directory.Delete(sarifDirectory, recursive: true);
        }

        ToolResult result = await _builder.BuildAsync(projectPath, sarifDirectory, noIncremental, noDependencies, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<string> logs = Directory.Exists(sarifDirectory)
            ? Directory.GetFiles(sarifDirectory, "*.sarif").Order(StringComparer.Ordinal).ToArray()
            : [];
        return (result, logs);
    }

    private static async Task<List<ReportedIssue>> ToIssuesAsync(
        GitRepository git,
        StateStore state,
        TurnSnapshot snapshot,
        MoveSources moveSources,
        IEnumerable<(ProjectWork Project, SarifDiagnostic Diagnostic)> diagnostics,
        IReadOnlyDictionary<string, IReadOnlySet<int>> changedLines,
        IReadOnlyDictionary<RulesManifest, IReadOnlySet<string>> registered,
        CancellationToken cancellationToken)
    {
        var sourceLines = new Dictionary<string, string[]>(ProjectFiles.PathComparer);
        var trees = new Dictionary<string, SyntaxTree>(ProjectFiles.PathComparer);
        var baselines = new Dictionary<string, BaselineDeclarations?>(ProjectFiles.PathComparer);
        var seen = new HashSet<(string, int, string, string)>();
        var issues = new List<ReportedIssue>();
        foreach ((ProjectWork project, SarifDiagnostic diagnostic) in diagnostics)
        {
            if (!DiagnosticFilter.IsReported(diagnostic, changedLines, registered[project.Manifest]))
            {
                continue;
            }

            // Multi-targeting projects log the same diagnostic once per framework.
            string path = Path.GetRelativePath(git.TopLevel, diagnostic.FilePath).Replace('\\', '/');
            if (!seen.Add((path, diagnostic.Line, diagnostic.RuleId, diagnostic.Message)))
            {
                continue;
            }

            if (!sourceLines.TryGetValue(diagnostic.FilePath, out string[]? lines))
            {
                lines = File.ReadAllLines(diagnostic.FilePath);
                sourceLines.Add(diagnostic.FilePath, lines);
            }

            string? source = diagnostic.Line >= 1 && diagnostic.Line <= lines.Length ? lines[diagnostic.Line - 1].Trim() : null;
            (IReadOnlyList<NamingSymbolKind>, NamingFailure)? naming = diagnostic.RuleId == DeclarationLocator.NamingDiagnosticId
                ? NamingOf(diagnostic, trees)
                : null;
            bool existingApi = project.Manifest.BreakingFixDiagnostics.Contains(diagnostic.RuleId, StringComparer.OrdinalIgnoreCase) &&
                await IsExistingApiAsync(git, state, snapshot, moveSources, path, diagnostic, trees, baselines, cancellationToken).ConfigureAwait(false);
            issues.Add(new ReportedIssue(
                path,
                diagnostic.Line,
                diagnostic.RuleId,
                diagnostic.Message,
                source,
                StopReport.RulesFor(project.Manifest, diagnostic.RuleId, diagnostic.Message, source, project.Info.Profile, naming),
                existingApi));
        }

        return StopReport.Sort(issues).ToList();
    }

    /// <summary>
    /// Whether the diagnostic is about a declaration the file already had at the start of the turn. False when the
    /// baseline cannot be read or parsed, so the issue is reported as before, and for a diagnostic on a type whose
    /// base list the turn changed, unless it is a naming one (<see cref="ExistingApi.ComparesBaseList"/>).
    /// </summary>
    private static async Task<bool> IsExistingApiAsync(
        GitRepository git,
        StateStore state,
        TurnSnapshot snapshot,
        MoveSources moveSources,
        string path,
        SarifDiagnostic diagnostic,
        Dictionary<string, SyntaxTree> trees,
        Dictionary<string, BaselineDeclarations?> baselines,
        CancellationToken cancellationToken)
    {
        if (!baselines.TryGetValue(diagnostic.FilePath, out BaselineDeclarations? declarations))
        {
            try
            {
                string baselinePath = await Snapshot.WriteBaselineAsync(git, state, snapshot, path, moveSources, cancellationToken)
                    .ConfigureAwait(false);
                SyntaxTree baseline = DeclarationLocator.Parse(await File.ReadAllTextAsync(baselinePath, cancellationToken).ConfigureAwait(false));
                declarations = DeclarationLocator.Baseline(baseline);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                declarations = null;
            }

            baselines.Add(diagnostic.FilePath, declarations);
        }

        return ExistingApi.IsExisting(
            TreeOf(diagnostic.FilePath, trees),
            declarations,
            diagnostic.Line,
            diagnostic.Column,
            compareBaseList: ExistingApi.ComparesBaseList(diagnostic.RuleId));
    }

    /// <summary>The file's current text, parsed once; from the text rather than its lines, since Roslyn counts line breaks that ReadAllLines does not.</summary>
    private static SyntaxTree TreeOf(string filePath, Dictionary<string, SyntaxTree> trees)
    {
        if (!trees.TryGetValue(filePath, out SyntaxTree? tree))
        {
            tree = DeclarationLocator.Parse(File.ReadAllText(filePath));
            trees.Add(filePath, tree);
        }

        return tree;
    }

    /// <summary>What IDE1006 flagged: the kinds of the declared symbol and how its name fails; null if either is unknown.</summary>
    private static (IReadOnlyList<NamingSymbolKind>, NamingFailure)? NamingOf(SarifDiagnostic diagnostic, Dictionary<string, SyntaxTree> trees)
    {
        NamingFailure? failure = DeclarationLocator.ClassifyNamingFailure(diagnostic.Message);
        if (failure is null)
        {
            return null;
        }

        IReadOnlyList<NamingSymbolKind>? kinds = DeclarationLocator.NamingKindsAt(TreeOf(diagnostic.FilePath, trees), diagnostic.Line, diagnostic.Column);
        return kinds is null ? null : (kinds, failure.Value);
    }

    /// <summary>
    /// A failed build is never passed over: unless the report holds a compile error of the project and none were
    /// left out, one line says so, naming the first compile error in a file not changed this turn if there is one.
    /// A build that compiled and only failed to copy its output, as when the app is running, says the diagnostics
    /// were checked.
    /// </summary>
    private static string? BuildFailure(
        GitRepository git,
        ProjectWork project,
        ToolResult build,
        bool logsRead,
        IEnumerable<(ProjectWork Project, SarifDiagnostic Diagnostic)> diagnostics,
        IReadOnlyDictionary<string, IReadOnlySet<int>> changedLines,
        IReadOnlySet<string> registered)
    {
        SarifDiagnostic[] errors = diagnostics
            .Where(d => d.Project == project && DiagnosticFilter.IsCompileError(d.Diagnostic))
            .Select(d => d.Diagnostic)
            .Distinct()
            .ToArray();
        SarifDiagnostic[] unreported = errors
            .Where(d => !DiagnosticFilter.IsReported(d, changedLines, registered))
            .OrderBy(d => d.FilePath, StringComparer.Ordinal)
            .ThenBy(d => d.Line)
            .ToArray();
        if (unreported.Length == 0)
        {
            if (errors.Length > 0)
            {
                return null;
            }

            return logsRead && IsCopyFailure(build)
                ? "FDG: output copy failed (is the app running?); diagnostics were checked."
                : Failure(git, "dotnet build", project.Info.ProjectPath, build);
        }

        SarifDiagnostic first = unreported[0];
        return $"FDG: dotnet build failed for {Display(git, project.Info.ProjectPath)} (exit code {build.ExitCode}): " +
            $"{unreported.Length} compile error(s) in files not changed this turn, first: " +
            $"{Display(git, first.FilePath)}:{first.Line} {first.RuleId} {first.Message}";
    }

    /// <summary>
    /// Diffs the AI's version of each file with the formatted one and stores the changed ranges, joined with what
    /// earlier Stops of the same turn recorded: a file they formatted is found already formatted by this one.
    /// </summary>
    private static async Task<AutofixRecord?> RecordAutofixAsync(
        GitRepository git,
        StateStore state,
        int turn,
        IReadOnlyList<ProjectWork> projects,
        CancellationToken cancellationToken)
    {
        var files = new List<AutofixFile>();
        foreach (string path in projects.SelectMany(p => p.Files).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            string diff = await git.DiffFilesAsync(
                state.AiVersionPath(path),
                Snapshot.FullPath(git, path),
                ignoreWhitespace: false,
                cancellationToken).ConfigureAwait(false);
            IReadOnlyList<LineRange> ranges = ChangedLines.ParseHunks(diff);
            if (ranges.Count > 0)
            {
                int lineCount = File.ReadLines(Snapshot.FullPath(git, path)).Count();
                files.Add(new AutofixFile(path, ranges, lineCount));
            }
        }

        AutofixRecord? previous = state.ReadAutofix();
        AutofixRecord? record = files.Count == 0
            ? previous is not null && previous.Turn == turn ? previous : null
            : AutofixRecord.Merge(previous, new AutofixRecord(turn, files));
        state.WriteAutofix(record);
        return record;
    }

    /// <summary>The build's only errors are MSB3021, MSB3026 or MSB3027: copying a file to the output failed.</summary>
    private static bool IsCopyFailure(ToolResult build)
    {
        string[] errors = BuildError().Matches(build.Output).Select(m => m.Groups["code"].Value).ToArray();
        return errors.Length > 0 && errors.All(code => code is "MSB3021" or "MSB3026" or "MSB3027");
    }

    // "error CODE:", or "error :" without a code, as a custom Error task writes it.
    [GeneratedRegex(@"\berror(\s+(?<code>[A-Z]+[0-9]+))?\s*:")]
    private static partial Regex BuildError();

    private static string Failure(GitRepository git, string tool, string projectPath, ToolResult result)
    {
        string detail = result.FirstLines(int.MaxValue).FirstOrDefault(l => l.Contains("error", StringComparison.OrdinalIgnoreCase))
            ?? result.FirstLines(1).FirstOrDefault()
            ?? string.Empty;
        string message = $"FDG: {tool} failed for {Display(git, projectPath)} (exit code {result.ExitCode})";
        return detail.Length == 0 ? message + "." : message + ": " + detail.Trim();
    }

    private static string Display(GitRepository git, string path) =>
        Path.GetRelativePath(git.TopLevel, path).Replace('\\', '/');

    private static string HashFiles(IEnumerable<string> files)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string file in files)
        {
            hash.AppendData(File.Exists(file) ? SHA256.HashData(File.ReadAllBytes(file)) : []);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private sealed record ProjectWork(ProjectInfo Info, RulesManifest Manifest, List<string> Files);

    /// <summary>
    /// A build of one project: its result, the logs it wrote and what they hold, and whether it skipped the projects
    /// the project references.
    /// </summary>
    private sealed record ProjectBuild(
        ToolResult Build,
        IReadOnlyList<string> Logs,
        IReadOnlyList<SarifDiagnostic> Diagnostics,
        bool Unreadable,
        bool NoDependencies);
}
