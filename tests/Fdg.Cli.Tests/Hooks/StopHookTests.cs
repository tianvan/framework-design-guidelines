using System.Text.Json;
using Fdg.Cli.Commands;
using Fdg.Cli.Hooks;
using Fdg.Cli.Projects;
using Fdg.Cli.Tooling;

namespace Fdg.Cli.Tests.Hooks;

public sealed class StopHookTests : IDisposable
{
    private const string Project = "<Project Sdk=\"Microsoft.NET.Sdk\" />\n";

    private const string Session = "session/with:odd*chars";

    private readonly GitTestRepo _repo = new();
    private readonly FakeHookProjects _projects = new();
    private readonly FakeHookFormatter _formatter = new();
    private readonly FakeHookBuilder _builder = new();
    private readonly Dictionary<string, string> _environment = [];
    private readonly string _project;

    public StopHookTests()
    {
        _project = _repo.Write("src/中文 App/App.csproj", Project);
        _projects.Add(_project, ProjectState.InScope);
        _repo.Write("src/中文 App/Order.cs", Lines(10));
        _repo.CommitAll();
    }

    public void Dispose()
    {
        _repo.Dispose();
    }

    [Fact]
    public async Task Out_of_scope_and_obj_files_are_not_formatted_or_reported()
    {
        string legacy = _repo.Write("Legacy/Legacy.csproj", "<Project ToolsVersion=\"15.0\" />\n");
        _projects.Add(legacy, ProjectState.NotSdkStyle);
        string noProfile = _repo.Write("NoProfile/NoProfile.csproj", Project);
        _projects.Add(noProfile, ProjectState.NoProfile);
        string notRestored = _repo.Write("NotRestored/NotRestored.csproj", Project);
        _projects.Add(notRestored, ProjectState.NotRestored);
        _repo.CommitAll();
        await TurnStartAsync();
        string[] outOfScope =
        [
            _repo.Write("Legacy/L.cs", "class L { }\n"),
            _repo.Write("NoProfile/N.cs", "class N { }\n"),
            _repo.Write("NotRestored/R.cs", "class R { }\n"),
            _repo.Write("Loose/NoProject.cs", "class Loose { }\n"),
            _repo.Write("src/中文 App/obj/Debug/Generated.cs", "class G { }\n"),
            _repo.Write("src/中文 App/bin/Copied.cs", "class C { }\n"),
        ];
        _builder.Sarif = _ => outOfScope.Select(f => new SarifResult("IDE1006", "warning", "Naming rule violation", f, 1)).ToArray();

        string output = await TurnStopAsync();

        Assert.Equal(string.Empty, output);
        Assert.Empty(_formatter.Calls);
        Assert.Empty(_builder.Calls);
    }

    [Fact]
    public async Task No_changes_this_turn_outputs_nothing()
    {
        _repo.Write("src/中文 App/Order.cs", Lines(10, (3, "int changedBeforeTheTurn;")));
        await TurnStartAsync();

        string output = await TurnStopAsync();

        Assert.Equal(string.Empty, output);
        Assert.Empty(_formatter.Calls);
        Assert.Empty(_builder.Calls);
    }

    [Fact]
    public async Task Only_registered_diagnostics_on_changed_lines_are_reported()
    {
        await TurnStartAsync();
        string order = _repo.Write("src/中文 App/Order.cs", Lines(10, (4, "private int count;")));
        _builder.Sarif = _ => [
            new SarifResult("IDE1006", "warning", "Naming rule violation: Missing prefix: '_'", order, 4),
            new SarifResult("IDE1006", "warning", "Naming rule violation: on an unchanged line", order, 7),
        ];

        string output = await TurnStopAsync();

        Assert.Equal("block", Json(output, "decision"));
        Assert.Equal(
            "FDG found 1 issue(s) in lines you changed this turn. Fix them before finishing.\n"
            + "src/中文 App/Order.cs:4 IDE1006 Naming rule violation: Missing prefix: '_'\n"
            + "    private int count;\n",
            Before(Json(output, "reason")!, "    FDG"));
        Assert.Contains("    FDGA055 DO use a prefix", Json(output, "reason")!);
        // Each diagnostic lists at most two of its rules.
        Assert.Equal(2, Json(output, "reason")!.Split('\n').Count(l => l.StartsWith("    FDG", StringComparison.Ordinal)));
        HookFormatCall format = Assert.Single(_formatter.Calls);
        Assert.Equal(["Order.cs"], format.IncludeFiles);
        Assert.Contains("IDE1006", format.ExcludeDiagnostics);
        HookBuildCall build = Assert.Single(_builder.Calls);
        Assert.Equal(_project, build.ProjectPath);
        Assert.False(build.NoIncremental);
        Assert.StartsWith(Path.Combine(_repo.Path(".git"), "fdg", "session_with_odd_chars", "sarif", "1") + Path.DirectorySeparatorChar, build.SarifDirectory);
    }

    [Fact]
    public async Task Compile_errors_in_changed_files_are_reported_on_any_line()
    {
        string other = _repo.Write("src/中文 App/Other.cs", Lines(5));
        _repo.CommitAll();
        await TurnStartAsync();
        string order = _repo.Write("src/中文 App/Order.cs", Lines(10, (2, "int y = x;")));
        _builder.Result = new ToolResult(1, "build failed", false);
        _builder.Sarif = _ => [
            new SarifResult("CS0103", "error", "The name 'x' does not exist in the current context", order, 9),
            new SarifResult("CS0246", "error", "In a file the AI did not change", other, 1),
        ];

        string output = await TurnStopAsync();

        Assert.Equal(
            "FDG found 1 issue(s) in lines you changed this turn. Fix them before finishing.\n"
            + "src/中文 App/Order.cs:9 CS0103 The name 'x' does not exist in the current context\n"
            + "    // line 9\n",
            Json(output, "reason")!);
    }

    [Fact]
    public async Task Suggestions_and_unregistered_warnings_are_ignored()
    {
        await TurnStartAsync();
        string order = _repo.Write("src/中文 App/Order.cs", Lines(10, (4, "string? s = null;")));
        _builder.Sarif = _ => [
            new SarifResult("IDE1006", "note", "A suggestion", order, 4),
            new SarifResult("CS8600", "warning", "Converting null literal or possible null value", order, 4),
            new SarifResult("CA2000", "warning", "Dispose objects before losing scope", order, 4),
            new SarifResult("IDE0055", "warning", "Fix formatting", order, 4) { Suppressed = true },
        ];

        string output = await TurnStopAsync();

        Assert.Equal(string.Empty, output);
    }

    [Fact]
    public async Task Report_is_truncated_to_20_with_remaining_count()
    {
        await TurnStartAsync();
        string order = _repo.Write("src/中文 App/Order.cs", Lines(30, Enumerable.Range(1, 27).Select(i => (i, $"int v{i};")).ToArray()));
        _builder.Sarif = _ => Enumerable.Range(1, 27)
            .Select(i => new SarifResult("CA1707", "warning", $"Remove the underscores {i}", order, i))
            .Reverse()
            .ToArray();

        string reason = Json(await TurnStopAsync(), "reason")!;

        string[] diagnostics = reason.Split('\n').Where(l => l.StartsWith("src/", StringComparison.Ordinal)).ToArray();
        Assert.Equal(20, diagnostics.Length);
        Assert.StartsWith("src/中文 App/Order.cs:1 CA1707", diagnostics[0]);
        Assert.StartsWith("src/中文 App/Order.cs:20 CA1707", diagnostics[19]);
        Assert.StartsWith("FDG found 27 issue(s)", reason);
        Assert.EndsWith("\n... and 7 more.\n", reason);
    }

    [Fact]
    public async Task Third_stop_in_same_turn_allows_finish_with_system_message()
    {
        await TurnStartAsync();
        string order = _repo.Write("src/中文 App/Order.cs", Lines(10, (4, "int Bad_Name;")));
        _builder.Sarif = _ => [new SarifResult("CA1707", "warning", "Remove the underscores", order, 4)];

        string first = await TurnStopAsync();
        string second = await TurnStopAsync();
        string third = await TurnStopAsync();

        Assert.Equal("block", Json(first, "decision"));
        Assert.Equal("block", Json(second, "decision"));
        Assert.Null(Json(third, "decision"));
        Assert.StartsWith("FDG found 1 issue(s) in lines you changed this turn.", Json(third, "systemMessage")!);
        Assert.Contains("src/中文 App/Order.cs:4 CA1707", Json(third, "systemMessage")!);
    }

    [Fact]
    public async Task Missing_sarif_triggers_no_incremental_rebuild()
    {
        await TurnStartAsync();
        string order = _repo.Write("src/中文 App/Order.cs", Lines(10, (4, "int Bad_Name;")));
        // An up-to-date project skips CoreCompile and writes no log; only the forced rebuild does.
        _builder.Sarif = call => call.NoIncremental
            ? [new SarifResult("CA1707", "warning", "Remove the underscores", order, 4)]
            : null;

        string output = await TurnStopAsync();

        Assert.Equal([false, true], _builder.Calls.Select(c => c.NoIncremental));
        Assert.Equal("block", Json(output, "decision"));
        Assert.Contains("Order.cs:4 CA1707", Json(output, "reason")!);
    }

    [Fact]
    public async Task Sarif_uri_percent_encoding_is_decoded()
    {
        await TurnStartAsync();
        string order = _repo.Write("src/中文 App/Order.cs", Lines(10, (4, "int Bad_Name;")));
        string uri = new Uri(order).AbsoluteUri;
        Assert.Contains("%20", uri);
        Assert.Contains("%E4%B8%AD", uri);
        _builder.Sarif = _ => [new SarifResult("CA1707", "warning", "Remove the underscores", order, 4, uri)];

        string output = await TurnStopAsync();

        Assert.Contains("src/中文 App/Order.cs:4 CA1707", Json(output, "reason")!);
    }

    [Fact]
    public async Task Build_failure_without_sarif_reports_one_line_and_allows_finish()
    {
        await TurnStartAsync();
        _repo.Write("src/中文 App/Order.cs", Lines(10, (4, "int x;")));
        _builder.Result = new ToolResult(1, "\nerror MSB4018: The task failed unexpectedly.\nmore detail\n", false);
        _builder.Sarif = _ => null;

        string output = await TurnStopAsync();

        Assert.Equal(
            "FDG: dotnet build failed for src/中文 App/App.csproj (exit code 1): error MSB4018: The task failed unexpectedly.",
            Json(output, "systemMessage")!);
        Assert.Single(_builder.Calls);
    }

    [Fact]
    public async Task Timeout_reports_one_line_and_allows_finish()
    {
        _environment["FDG_HOOK_TIMEOUT_SECONDS"] = "1";
        await TurnStartAsync();
        _repo.Write("src/中文 App/Order.cs", Lines(10, (4, "int x;")));
        _builder.Delay = TimeSpan.FromSeconds(5);

        var watch = System.Diagnostics.Stopwatch.StartNew();
        string output = await TurnStopAsync();

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(4), watch.Elapsed.ToString());
        Assert.Equal(
            "FDG: the Stop check timed out after 1 s (FDG_HOOK_TIMEOUT_SECONDS); this turn was not checked.",
            Json(output, "systemMessage")!);
    }

    [Fact]
    public async Task FDG_HOOK_BUILD_0_skips_build_but_still_formats()
    {
        _environment["FDG_HOOK_BUILD"] = "0";
        await TurnStartAsync();
        _repo.Write("src/中文 App/Order.cs", Lines(10, (4, "int x;")));

        string output = await TurnStopAsync();

        Assert.Equal(string.Empty, output);
        Assert.Single(_formatter.Calls);
        Assert.Empty(_builder.Calls);
    }

    [Fact]
    public async Task Autofixed_ranges_are_recorded_for_next_turn()
    {
        await TurnStartAsync();
        string order = _repo.Write("src/中文 App/Order.cs", Lines(10, (4, "int x;"), (8, "int y;")));
        _formatter.OnFormat = _ => File.WriteAllText(order, Lines(10, (4, "int x;"), (8, "private int y;")));

        string stop = await TurnStopAsync();
        StateStore state = await StateAsync();
        AutofixRecord record = state.ReadAutofix()!;
        string next = await TurnStartAsync(HostKind.Codex);

        Assert.Equal(string.Empty, stop);
        AutofixFile file = Assert.Single(record.Files);
        Assert.Equal("src/中文 App/Order.cs", file.Path);
        Assert.Equal([new LineRange(8, 8)], file.Ranges);
        Assert.Equal(
            "FDG reformatted files after your last turn; re-read them before editing: src/中文 App/Order.cs (line 8).",
            JsonDocument.Parse(next).RootElement.GetProperty("hookSpecificOutput").GetProperty("additionalContext").GetString());
        Assert.Null(state.ReadAutofix());
    }

    [Fact]
    public async Task Claude_turn_start_does_not_repeat_autofix_summary()
    {
        await TurnStartAsync();
        string order = _repo.Write("src/中文 App/Order.cs", Lines(10, (8, "int y;")));
        _formatter.OnFormat = _ => File.WriteAllText(order, Lines(10, (8, "private int y;")));
        await TurnStopAsync();

        Assert.Equal(string.Empty, await TurnStartAsync(HostKind.Claude));
    }

    [Fact]
    public async Task Not_a_git_repository_outputs_nothing_for_turn_hooks()
    {
        using var tree = new TempTree();
        string project = tree.Write("App/App.csproj", Project);
        _projects.Add(project, ProjectState.InScope);
        tree.Write("App/A.cs", "class A { }\n");
        HookRunner runner = CreateRunner();
        var input = new HookInput(HostKind.Claude, Session, tree.Root, "UserPromptSubmit");

        Assert.Equal(string.Empty, await runner.TurnStartAsync(input, CancellationToken.None));
        Assert.Equal(string.Empty, await runner.TurnStopAsync(input with { EventName = "Stop" }, CancellationToken.None));
        Assert.Empty(_formatter.Calls);
    }

    [Fact]
    public async Task Session_start_outside_git_with_projects_says_checks_are_disabled()
    {
        using var tree = new TempTree();
        var input = new HookInput(HostKind.Codex, Session, tree.Root, "SessionStart");

        Assert.Equal(string.Empty, await CreateRunner().SessionStartAsync(input, CancellationToken.None));
        tree.Write("App/App.csproj", Project);
        Assert.Equal(
            "FDG: not a git repository, so the end-of-turn checks are disabled.",
            Json(await CreateRunner().SessionStartAsync(input, CancellationToken.None), "systemMessage"));
        Assert.Equal(
            string.Empty,
            await CreateRunner().SessionStartAsync(input with { Cwd = _repo.Root }, CancellationToken.None));
    }

    [Fact]
    public async Task Turn_start_resets_block_counter()
    {
        await TurnStartAsync();
        string order = _repo.Write("src/中文 App/Order.cs", Lines(10, (4, "int Bad_Name;")));
        _builder.Sarif = _ => [new SarifResult("CA1707", "warning", "Remove the underscores", order, 4)];
        await TurnStopAsync();
        await TurnStopAsync();

        await TurnStartAsync();
        _repo.Write("src/中文 App/Order.cs", Lines(10, (4, "int Bad_Name;"), (5, "int Other_Name;")));
        _builder.Sarif = _ => [new SarifResult("CA1707", "warning", "Remove the underscores", order, 5)];
        string output = await TurnStopAsync();

        Assert.Equal("block", Json(output, "decision"));
    }

    [Fact]
    public async Task Missing_snapshot_reports_one_line()
    {
        _repo.Write("src/中文 App/Order.cs", Lines(10, (4, "int x;")));

        string output = await TurnStopAsync();

        Assert.Equal(
            "FDG: no snapshot from the start of this turn; the Stop check was skipped.",
            Json(output, "systemMessage")!);
    }

    [Fact]
    public async Task Hook_command_turns_unexpected_errors_into_a_system_message()
    {
        _projects.Failure = new InvalidOperationException("boom");
        await TurnStartAsync();
        _repo.Write("src/中文 App/Order.cs", Lines(10, (4, "int x;")));
        var stdout = new StringWriter();
        string stdin = JsonSerializer.Serialize(new { session_id = Session, cwd = _repo.Root, hook_event_name = "Stop" });

        int exitCode = await HookCommands.RunAsync(
            "turn-stop",
            new StringReader(stdin),
            stdout,
            new StringWriter(),
            CreateRunner(),
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal("FDG hook error: boom", Json(stdout.ToString(), "systemMessage")!);
    }

    [Fact]
    public async Task Hook_command_rejects_unparsable_input_with_exit_code_1()
    {
        var stderr = new StringWriter();

        int exitCode = await HookCommands.RunAsync(
            "turn-stop",
            new StringReader("not json"),
            new StringWriter(),
            stderr,
            CreateRunner(),
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.StartsWith("FDG hook error: hook input is not JSON", stderr.ToString());
    }

    /// <summary>Lines <c>// line 1</c>..<c>// line n</c> with some lines replaced, LF-terminated.</summary>
    private static string Lines(int count, params (int Line, string Text)[] replacements) =>
        string.Concat(Enumerable.Range(1, count).Select(i =>
            (replacements.FirstOrDefault(r => r.Line == i).Text ?? $"// line {i}") + "\n"));

    private static string? Json(string output, string property)
    {
        Assert.NotEqual(string.Empty, output);
        using JsonDocument document = JsonDocument.Parse(output);
        return document.RootElement.TryGetProperty(property, out JsonElement value) ? value.GetString() : null;
    }

    private static string Before(string text, string marker)
    {
        int index = text.IndexOf(marker, StringComparison.Ordinal);
        return index < 0 ? text : text[..index];
    }

    private HookRunner CreateRunner() =>
        new(_projects, _formatter, _builder, name => _environment.GetValueOrDefault(name));

    private Task<string> TurnStartAsync(HostKind host = HostKind.Claude) =>
        CreateRunner().TurnStartAsync(new HookInput(host, Session, _repo.Root, "UserPromptSubmit"), CancellationToken.None);

    private Task<string> TurnStopAsync() =>
        CreateRunner().TurnStopAsync(new HookInput(HostKind.Claude, Session, _repo.Root, "Stop"), CancellationToken.None);

    private async Task<StateStore> StateAsync() =>
        StateStore.For((await GitRepository.OpenAsync(_repo.Root, CancellationToken.None))!, Session);
}
