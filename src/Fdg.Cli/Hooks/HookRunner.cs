using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Fdg.Cli.Commands;
using Fdg.Cli.Projects;
using Fdg.Cli.Rules;
using Fdg.Cli.Tooling;

namespace Fdg.Cli.Hooks;

/// <summary>
/// The hook logic. Every method returns the hook's stdout: <see cref="HookOutput.None"/> or one JSON object.
/// Nothing is written outside the state directory except the files dotnet format changes.
/// </summary>
public sealed class HookRunner
{
    private static readonly TimeSpan CleanupGrace = TimeSpan.FromSeconds(2);

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
    /// to the user. Outside a git repository the session's directory stands in for the repository root.
    /// </summary>
    public async Task<string> SessionStartAsync(HookInput input, CancellationToken cancellationToken)
    {
        GitRepository? git = await GitRepository.OpenAsync(input.Cwd, cancellationToken).ConfigureAwait(false);
        SessionReport? report = _session.Inspect(git?.TopLevel ?? input.Cwd);
        if (report is null)
        {
            return HookOutput.None;
        }

        return HookOutput.Context(
            input.EventName,
            report.Text,
            report.Issues.Count == 0 ? null : string.Join("\n", report.Issues));
    }

    /// <summary>
    /// UserPromptSubmit: snapshots the working tree and resets the block count. On Codex, which does not notice
    /// files changed behind its back, also tells the AI what the last Stop hook reformatted.
    /// </summary>
    public async Task<string> TurnStartAsync(HookInput input, CancellationToken cancellationToken)
    {
        GitRepository? git = await GitRepository.OpenAsync(input.Cwd, cancellationToken).ConfigureAwait(false);
        if (git is null)
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

    /// <summary>Stop: formats, builds and reports the turn's changes (design §5.4), within the configured timeout.</summary>
    public async Task<string> TurnStopAsync(HookInput input, CancellationToken cancellationToken)
    {
        GitRepository? git = await GitRepository.OpenAsync(input.Cwd, cancellationToken).ConfigureAwait(false);
        if (git is null)
        {
            return HookOutput.None;
        }

        StateStore state = StateStore.For(git, input.SessionId);
        TurnSnapshot? snapshot = state.ReadSnapshot();
        if (snapshot is null)
        {
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
        var notes = new List<string>();
        IReadOnlyList<ProjectWork> projects = GroupByProject(git, changes.Changed, notes);
        if (projects.Count == 0)
        {
            state.WriteAutofix(null);
            return Finish(notes, []);
        }

        foreach (string path in projects.SelectMany(p => p.Files))
        {
            string aiVersion = state.AiVersionPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(aiVersion)!);
            File.Copy(Snapshot.FullPath(git, path), aiVersion, overwrite: true);
        }

        // 2. Per project, in parallel: format.
        var formatNotes = new ConcurrentDictionary<int, string>();
        await Parallel.ForEachAsync(
            projects.Select((project, index) => (Project: project, Index: index)),
            cancellationToken,
            async (item, token) =>
            {
                if (await FormatProjectAsync(git, item.Project, token).ConfigureAwait(false) is string note)
                {
                    formatNotes[item.Index] = note;
                }
            }).ConfigureAwait(false);

        // 3-4. One project at a time, since builds of projects that reference each other would compile the shared
        // ones at once: build, read the SARIF logs.
        var diagnostics = new List<(ProjectWork Project, SarifDiagnostic Diagnostic)>();
        var failedBuilds = new List<(ProjectWork Project, ToolResult Build)>();
        for (int i = 0; i < projects.Count; i++)
        {
            string? note = formatNotes.GetValueOrDefault(i);
            if (settings.Build)
            {
                (ToolResult build, bool hasLogs, string? buildNote) = await BuildProjectAsync(
                    git, state, snapshot.Turn, projects[i], diagnostics, cancellationToken).ConfigureAwait(false);
                if (!build.Succeeded && hasLogs)
                {
                    failedBuilds.Add((projects[i], build));
                }

                note ??= buildNote;
            }

            if (note is not null)
            {
                notes.Add(note);
            }
        }

        // 6. What formatting changed, for the next turn (and for Codex when blocking now).
        AutofixRecord? autofix = await RecordAutofixAsync(git, state, snapshot.Turn, projects, cancellationToken).ConfigureAwait(false);

        // 4-5. Filter on the lines the AI changed since the turn started, then report.
        IReadOnlyDictionary<string, IReadOnlySet<int>> changedLines = await ComputeChangedLinesAsync(
            git, state, snapshot, new MoveSources(git, state, snapshot, changes.Deleted), projects, cancellationToken).ConfigureAwait(false);

        var registered = new Dictionary<RulesManifest, IReadOnlySet<string>>(ReferenceEqualityComparer.Instance);
        foreach (RulesManifest manifest in projects.Select(p => p.Manifest))
        {
            registered.TryAdd(manifest, manifest.RegisteredDiagnostics.ToHashSet(StringComparer.Ordinal));
        }

        foreach ((ProjectWork project, ToolResult build) in failedBuilds)
        {
            if (BuildFailure(git, project, build, diagnostics, changedLines, registered[project.Manifest]) is string note)
            {
                notes.Add(note);
            }
        }

        List<ReportedIssue> issues = ToIssues(git, diagnostics, changedLines, registered);
        if (issues.Count == 0)
        {
            return Finish(notes, []);
        }

        // stop_hook_active false means the host is not continuing after a block, so this is a new turn even if
        // UserPromptSubmit never reset the count. A payload without the field keeps the count, so the limit holds.
        int blocks = input.StopHookActive == false ? 0 : state.ReadBlocks();
        string count = $"FDG found {issues.Count} issue(s) in lines you changed this turn.";
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
        var aiLines = new Dictionary<string, int[]>(state.ReadAiLines(), StringComparer.Ordinal);
        var changedLines = new Dictionary<string, IReadOnlySet<int>>(ProjectFiles.PathComparer);
        foreach (string path in projects.SelectMany(p => p.Files).Distinct(StringComparer.Ordinal))
        {
            string current = Snapshot.FullPath(git, path);
            string aiVersion = state.AiVersionPath(path);
            string previous = state.FormattedPath(path);
            IReadOnlySet<int> lines;
            if (aiLines.TryGetValue(path, out int[]? previousLines) && File.Exists(previous))
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
            File.Copy(current, previous, overwrite: true);
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
    /// at once would race). Files under no project, or under an out-of-scope or unrestored project, are skipped.
    /// </summary>
    private List<ProjectWork> GroupByProject(GitRepository git, IReadOnlyList<string> changed, List<string> notes)
    {
        var infos = new Dictionary<string, ProjectInfo?>(ProjectFiles.PathComparer);
        var work = new Dictionary<string, ProjectWork>(ProjectFiles.PathComparer);
        foreach (string path in changed)
        {
            string directory = Path.GetDirectoryName(Snapshot.FullPath(git, path))!;
            foreach (string projectPath in ProjectFiles.FindOwningProjects(directory))
            {
                if (!infos.TryGetValue(projectPath, out ProjectInfo? info))
                {
                    info = GetInScope(git, projectPath, notes);
                    infos.Add(projectPath, info);
                }

                if (info is not null)
                {
                    if (!work.TryGetValue(projectPath, out ProjectWork? project))
                    {
                        project = new ProjectWork(info, RulesManifest.LoadOrEmbedded(info.RulesPath), []);
                        work.Add(projectPath, project);
                    }

                    project.Files.Add(path);
                    break;
                }
            }
        }

        return work.Values.ToList();
    }

    private ProjectInfo? GetInScope(GitRepository git, string projectPath, List<string> notes)
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

        return info;
    }

    /// <returns>A one-line note when formatting failed.</returns>
    private async Task<string?> FormatProjectAsync(GitRepository git, ProjectWork project, CancellationToken cancellationToken)
    {
        string projectPath = project.Info.ProjectPath;
        string[] files = project.Files.Select(f => Snapshot.FullPath(git, f)).ToArray();
        ToolResult format = await FormatPasses.RunAsync(
            _formatter,
            projectPath,
            FormatIncludes.ToProjectRelative(projectPath, files),
            project.Manifest.BreakingFixDiagnostics,
            () => HashFiles(files),
            cancellationToken).ConfigureAwait(false);
        return format.Succeeded ? null : Failure(git, "dotnet format", projectPath, format);
    }

    /// <returns>
    /// The build result, whether it wrote SARIF logs, and a one-line note when the project's diagnostics could not
    /// be read.
    /// </returns>
    private async Task<(ToolResult Build, bool HasLogs, string? Note)> BuildProjectAsync(
        GitRepository git,
        StateStore state,
        int turn,
        ProjectWork project,
        List<(ProjectWork, SarifDiagnostic)> diagnostics,
        CancellationToken cancellationToken)
    {
        string projectPath = project.Info.ProjectPath;
        string sarifDirectory = state.SarifDirectory(turn, projectPath);
        (ToolResult build, IReadOnlyList<string> logs) = await BuildAsync(projectPath, sarifDirectory, noIncremental: false, cancellationToken)
            .ConfigureAwait(false);
        (IReadOnlyList<SarifDiagnostic> found, bool unreadable) = ReadLogs(logs, projectPath);
        if (build.Succeeded && (logs.Count == 0 || IsForeign(found, projectPath)))
        {
            // Up to date: CoreCompile was skipped and wrote no log, or only a referenced project wrote one (a
            // package without FdgSarifProject support). That is not "no issues".
            (build, logs) = await BuildAsync(projectPath, sarifDirectory, noIncremental: true, cancellationToken).ConfigureAwait(false);
            (found, unreadable) = ReadLogs(logs, projectPath);
        }

        if (logs.Count == 0)
        {
            return (build, false, build.Succeeded
                ? $"FDG: dotnet build wrote no SARIF log for {Display(git, projectPath)}; its diagnostics were not checked."
                : Failure(git, "dotnet build", projectPath, build));
        }

        diagnostics.AddRange(found.Select(d => (project, d)));
        return (build, true, unreadable
            ? $"FDG: cannot read the SARIF log of {Display(git, projectPath)}; its diagnostics were not checked."
            : null);
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

    /// <summary>A log with results, none of them in a file of the project, is a referenced project's.</summary>
    private static bool IsForeign(IReadOnlyList<SarifDiagnostic> diagnostics, string projectPath)
    {
        var owners = new Dictionary<string, bool>(ProjectFiles.PathComparer);
        return diagnostics.Count > 0 && !diagnostics.Any(d =>
        {
            string directory = Path.GetDirectoryName(d.FilePath) ?? string.Empty;
            if (!owners.TryGetValue(directory, out bool owned))
            {
                owned = Directory.Exists(directory) &&
                    ProjectFiles.FindOwningProjects(directory).Contains(projectPath, ProjectFiles.PathComparer);
                owners.Add(directory, owned);
            }

            return owned;
        });
    }

    /// <returns>The build result and the SARIF logs it wrote, one per target framework.</returns>
    private async Task<(ToolResult Result, IReadOnlyList<string> Logs)> BuildAsync(
        string projectPath,
        string sarifDirectory,
        bool noIncremental,
        CancellationToken cancellationToken)
    {
        // Logs of an earlier Stop in this turn must not pass for this build's.
        if (Directory.Exists(sarifDirectory))
        {
            Directory.Delete(sarifDirectory, recursive: true);
        }

        ToolResult result = await _builder.BuildAsync(projectPath, sarifDirectory, noIncremental, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<string> logs = Directory.Exists(sarifDirectory)
            ? Directory.GetFiles(sarifDirectory, "*.sarif").Order(StringComparer.Ordinal).ToArray()
            : [];
        return (result, logs);
    }

    private static List<ReportedIssue> ToIssues(
        GitRepository git,
        IEnumerable<(ProjectWork Project, SarifDiagnostic Diagnostic)> diagnostics,
        IReadOnlyDictionary<string, IReadOnlySet<int>> changedLines,
        IReadOnlyDictionary<RulesManifest, IReadOnlySet<string>> registered)
    {
        var sourceLines = new Dictionary<string, string[]>(ProjectFiles.PathComparer);
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
            issues.Add(new ReportedIssue(
                path,
                diagnostic.Line,
                diagnostic.RuleId,
                diagnostic.Message,
                source,
                StopReport.RulesFor(project.Manifest, diagnostic.RuleId, diagnostic.Message, source, project.Info.Profile)));
        }

        return StopReport.Sort(issues).ToList();
    }

    /// <summary>
    /// A failed build is never passed over: unless the report holds a compile error of the project and none were
    /// left out, one line says so, naming the first compile error in a file not changed this turn if there is one.
    /// </summary>
    private static string? BuildFailure(
        GitRepository git,
        ProjectWork project,
        ToolResult build,
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
            return errors.Length > 0 ? null : Failure(git, "dotnet build", project.Info.ProjectPath, build);
        }

        SarifDiagnostic first = unreported[0];
        return $"FDG: dotnet build failed for {Display(git, project.Info.ProjectPath)} (exit code {build.ExitCode}): " +
            $"{unreported.Length} compile error(s) in files not changed this turn, first: " +
            $"{Display(git, first.FilePath)}:{first.Line} {first.RuleId} {first.Message}";
    }

    /// <summary>Diffs the AI's version of each file with the formatted one and stores the changed ranges.</summary>
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
                files.Add(new AutofixFile(path, ranges));
            }
        }

        AutofixRecord? record = files.Count == 0 ? null : new AutofixRecord(turn, files);
        state.WriteAutofix(record);
        return record;
    }

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
}
