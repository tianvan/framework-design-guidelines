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
    private readonly Func<string, string?> _environment;

    public HookRunner(
        IProjectInfoProvider projects,
        IFormatRunner formatter,
        IBuildRunner builder,
        Func<string, string?> environment)
    {
        _projects = projects;
        _formatter = formatter;
        _builder = builder;
        _environment = environment;
    }

    /// <summary>
    /// Outside a git repository the turn hooks are off; says so once per session when C# projects are in sight.
    /// The rest of SessionStart (context injection, self-check) is not part of this hook core yet.
    /// </summary>
    public async Task<string> SessionStartAsync(HookInput input, CancellationToken cancellationToken)
    {
        if (await GitRepository.OpenAsync(input.Cwd, cancellationToken).ConfigureAwait(false) is not null)
        {
            return HookOutput.None;
        }

        bool hasProjects = Directory.Exists(input.Cwd) && ProjectFiles.Enumerate(input.Cwd, "*.csproj").Any();
        return hasProjects
            ? HookOutput.SystemMessage("FDG: not a git repository, so the end-of-turn checks are disabled.")
            : HookOutput.None;
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
                "this turn was not checked.");
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
        IReadOnlyList<string> changed = await Snapshot.FindChangedFilesAsync(git, snapshot, cancellationToken).ConfigureAwait(false);
        var notes = new List<string>();
        IReadOnlyList<ProjectWork> projects = GroupByProject(git, changed, notes);
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

        // 2-4. Per project, in parallel: format, build, read the SARIF logs.
        var diagnostics = new ConcurrentBag<(ProjectWork Project, SarifDiagnostic Diagnostic)>();
        var projectNotes = new ConcurrentDictionary<int, string>();
        await Parallel.ForEachAsync(
            projects.Select((project, index) => (Project: project, Index: index)),
            cancellationToken,
            async (item, token) =>
            {
                string? note = await CheckProjectAsync(git, state, snapshot.Turn, settings, item.Project, diagnostics, token)
                    .ConfigureAwait(false);
                if (note is not null)
                {
                    projectNotes[item.Index] = note;
                }
            }).ConfigureAwait(false);
        notes.AddRange(projectNotes.OrderBy(n => n.Key).Select(n => n.Value));

        // 6. What formatting changed, for the next turn (and for Codex when blocking now).
        AutofixRecord? autofix = await RecordAutofixAsync(git, state, snapshot.Turn, projects, cancellationToken).ConfigureAwait(false);

        // 4-5. Filter on the lines changed since the turn started, then report.
        var changedLines = new Dictionary<string, IReadOnlySet<int>>(ProjectFiles.PathComparer);
        foreach (string path in projects.SelectMany(p => p.Files).Distinct(StringComparer.Ordinal))
        {
            string baseline = await Snapshot.WriteBaselineAsync(git, state, snapshot, path, cancellationToken).ConfigureAwait(false);
            string current = Snapshot.FullPath(git, path);
            changedLines[current] = await ChangedLines.ComputeAsync(git, baseline, current, cancellationToken).ConfigureAwait(false);
        }

        List<ReportedIssue> issues = ToIssues(git, diagnostics, changedLines);
        if (issues.Count == 0)
        {
            return Finish(notes, []);
        }

        int blocks = state.ReadBlocks();
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

    /// <returns>A one-line note when the project could not be checked completely.</returns>
    private async Task<string?> CheckProjectAsync(
        GitRepository git,
        StateStore state,
        int turn,
        HookSettings settings,
        ProjectWork project,
        ConcurrentBag<(ProjectWork, SarifDiagnostic)> diagnostics,
        CancellationToken cancellationToken)
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
        string? note = format.Succeeded ? null : Failure(git, "dotnet format", projectPath, format);
        if (!settings.Build)
        {
            return note;
        }

        string sarifDirectory = state.SarifDirectory(turn, projectPath);
        (ToolResult build, IReadOnlyList<string> logs) = await BuildAsync(projectPath, sarifDirectory, noIncremental: false, cancellationToken)
            .ConfigureAwait(false);
        if (logs.Count == 0 && build.Succeeded)
        {
            // Up to date: CoreCompile was skipped and wrote no log. That is not "no issues".
            (build, logs) = await BuildAsync(projectPath, sarifDirectory, noIncremental: true, cancellationToken).ConfigureAwait(false);
        }

        if (logs.Count == 0)
        {
            return note ?? (build.Succeeded
                ? $"FDG: dotnet build wrote no SARIF log for {Display(git, projectPath)}; its diagnostics were not checked."
                : Failure(git, "dotnet build", projectPath, build));
        }

        string projectDirectory = Path.GetDirectoryName(projectPath)!;
        foreach (string log in logs)
        {
            try
            {
                foreach (SarifDiagnostic diagnostic in SarifReader.Read(log, projectDirectory))
                {
                    diagnostics.Add((project, diagnostic));
                }
            }
            catch (JsonException)
            {
                note ??= $"FDG: cannot read the SARIF log of {Display(git, projectPath)}; its diagnostics were not checked.";
            }
        }

        return note;
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
        IReadOnlyDictionary<string, IReadOnlySet<int>> changedLines)
    {
        var sourceLines = new Dictionary<string, string[]>(ProjectFiles.PathComparer);
        var seen = new HashSet<(string, int, string, string)>();
        var issues = new List<ReportedIssue>();
        foreach ((ProjectWork project, SarifDiagnostic diagnostic) in diagnostics)
        {
            var registered = project.Manifest.RegisteredDiagnostics.ToHashSet(StringComparer.Ordinal);
            if (!DiagnosticFilter.IsReported(diagnostic, changedLines, registered))
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
