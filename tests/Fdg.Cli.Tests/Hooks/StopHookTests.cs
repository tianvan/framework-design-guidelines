using System.Text.Json;
using Fdg.Cli.Commands;
using Fdg.Cli.Hooks;
using Fdg.Cli.Projects;
using Fdg.Cli.Tooling;

namespace Fdg.Cli.Tests.Hooks;

public sealed class StopHookTests : IDisposable
{
    private const string Project = "<Project Sdk=\"Microsoft.NET.Sdk\" />\n";

    private const string OnboardedProject =
        "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
        "  <ItemGroup>\n" +
        "    <PackageReference Include=\"FrameworkDesignGuidelines.Analyzers\" PrivateAssets=\"all\" />\n" +
        "  </ItemGroup>\n" +
        "</Project>\n";

    private const string ChecksDisabled = "Not a git repository: end-of-turn checks are disabled.";

    private const string Session = "session/with:odd*chars";

    private readonly GitTestRepo _repo = new();
    private readonly FakeHookProjects _projects = new();
    private readonly FakeHookFormatter _formatter = new();
    private readonly FakeHookBuilder _builder = new();
    private readonly Dictionary<string, string> _environment = [];
    private readonly string _project;

    public StopHookTests()
    {
        _project = _repo.Write("src/中文 App/App.csproj", OnboardedProject);
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
    public async Task Onboarded_project_that_is_not_restored_adds_a_note()
    {
        string notRestored = _repo.Write("NotRestored/NotRestored.csproj", OnboardedProject);
        _projects.Add(notRestored, ProjectState.NotRestored);
        _repo.CommitAll();
        await TurnStartAsync();
        _repo.Write("NotRestored/R.cs", "class R { }\n");

        string output = await TurnStopAsync();

        Assert.Equal(
            "FDG: NotRestored/NotRestored.csproj is not restored (run dotnet restore); its files were not checked.",
            Json(output, "systemMessage"));
        Assert.Empty(_formatter.Calls);
    }

    [Fact]
    public async Task Project_whose_rules_json_has_a_newer_protocol_adds_a_note()
    {
        string newer = _repo.Write("Newer/Newer.csproj", OnboardedProject);
        string rules = _repo.Write(
            "packages/rules.json",
            """{ "protocol": 2, "version": "9.0.0", "rules": [], "registeredDiagnostics": [], "breakingFixDiagnostics": [] }""");
        _projects.Add(newer, ProjectState.InScope, rulesPath: rules);
        _repo.CommitAll();
        await TurnStartAsync();
        _repo.Write("Newer/N.cs", "class N { }\n");

        string output = await TurnStopAsync();

        Assert.Equal(
            "FDG: Newer/Newer.csproj: rules.json protocol 2 is newer than this dotnet-fdg supports (1); update it " +
            "with: dotnet tool update -g dotnet-fdg; its files were not checked.",
            Json(output, "systemMessage"));
        Assert.Empty(_formatter.Calls);
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
        string order = _repo.Write("src/中文 App/Order.cs", Lines(10, (1, "class Order {"), (4, "    private int count;"), (10, "}")));
        _builder.Sarif = _ => [
            new SarifResult("IDE1006", "warning", "Naming rule violation: Missing prefix: '_'", order, 4) { Column = 17 },
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
        // The prefix of a private field is explained by one rule only.
        Assert.Single(Json(output, "reason")!.Split('\n'), l => l.StartsWith("    FDG", StringComparison.Ordinal));
        HookFormatCall format = Assert.Single(_formatter.Calls);
        Assert.Equal(["Order.cs"], format.IncludeFiles);
        Assert.Contains("FDGA003", format.Diagnostics);
        Assert.DoesNotContain("IDE1006", format.Diagnostics);
        HookBuildCall build = Assert.Single(_builder.Calls);
        Assert.Equal(_project, build.ProjectPath);
        Assert.False(build.NoIncremental);
        Assert.StartsWith(Path.Combine(_repo.Path(".git"), "fdg", "session_with_odd_chars", "sarif", "1") + Path.DirectorySeparatorChar, build.SarifDirectory);
    }

    [Fact]
    public async Task Diagnostic_whose_declaration_cannot_be_located_quotes_no_rule()
    {
        // IDE1006 checks 13 rules; the flagged line is a fragment that does not parse, so the kind of symbol is unknown
        // and no rule is quoted.
        await TurnStartAsync();
        string order = _repo.Write("src/中文 App/Order.cs", Lines(10, (4, "public static string describe_failure(")));
        _builder.Sarif = _ => [
            new SarifResult(
                "IDE1006",
                "warning",
                "Naming rule violation: These words must begin with upper case characters: describe_failure",
                order,
                4),
        ];

        string reason = Json(await TurnStopAsync(), "reason")!;

        Assert.Equal(
            "FDG found 1 issue(s) in lines you changed this turn. Fix them before finishing.\n"
            + "src/中文 App/Order.cs:4 IDE1006 Naming rule violation: These words must begin with upper case characters: describe_failure\n"
            + "    public static string describe_failure(\n",
            reason);
    }

    [Fact]
    public async Task Lower_case_method_quotes_the_casing_rules_of_methods()
    {
        await TurnStartAsync();
        string order = _repo.Write(
            "src/中文 App/Order.cs",
            Lines(10, (1, "class Order {"), (4, "    public static string describe_failure() => \"\";"), (10, "}")));
        _builder.Sarif = _ => [
            new SarifResult(
                "IDE1006",
                "warning",
                "Naming rule violation: These words must begin with upper case characters: describe_failure",
                order,
                4) { Column = 26 },
        ];

        string reason = Json(await TurnStopAsync(), "reason")!;

        string[] quoted = reason.Split('\n').Where(l => l.StartsWith("    FDG", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, quoted.Length);
        Assert.Contains(quoted, l => l.StartsWith("    FDG3001 ", StringComparison.Ordinal));
        Assert.Contains(quoted, l => l.StartsWith("    FDGA052 ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Upper_case_parameter_quotes_only_parameter_rules()
    {
        await TurnStartAsync();
        string order = _repo.Write(
            "src/中文 App/Order.cs",
            Lines(10, (1, "class Order {"), (4, "    public void Ship(int Count) { }"), (10, "}")));
        _builder.Sarif = _ => [
            new SarifResult(
                "IDE1006",
                "warning",
                "Naming rule violation: The first word, 'Count', must begin with a lower case character",
                order,
                4) { Column = 26 },
        ];

        string reason = Json(await TurnStopAsync(), "reason")!;

        string[] quoted = reason.Split('\n').Where(l => l.StartsWith("    FDG", StringComparison.Ordinal)).ToArray();
        Assert.InRange(quoted.Length, 1, 2);
        Assert.All(quoted, l => Assert.Matches(@"^    (FDG3002|FDG3005|FDG3080|FDGA057) ", l));
    }

    [Fact]
    public async Task Prefix_on_a_public_field_quotes_FDG3079()
    {
        await TurnStartAsync();
        string order = _repo.Write(
            "src/中文 App/Order.cs",
            Lines(10, (1, "class Order {"), (4, "    public int _count;"), (10, "}")));
        _builder.Sarif = _ => [
            new SarifResult("IDE1006", "warning", "Naming rule violation: Prefix '_' is not expected", order, 4) { Column = 16 },
        ];

        string reason = Json(await TurnStopAsync(), "reason")!;

        string quoted = Assert.Single(reason.Split('\n'), l => l.StartsWith("    FDG", StringComparison.Ordinal));
        Assert.StartsWith("    FDG3079 ", quoted);
    }

    [Fact]
    public async Task Files_committed_during_the_turn_are_still_formatted_and_reported()
    {
        await TurnStartAsync();
        string order = _repo.Write("src/中文 App/Order.cs", Lines(10, (4, "int Bad_Name;")));
        _repo.CommitAll("the AI commits");
        _builder.Sarif = _ => [
            new SarifResult("CA1707", "warning", "Remove the underscores", order, 4),
            new SarifResult("CA1707", "warning", "On a line the AI did not change", order, 7),
        ];

        string reason = Json(await TurnStopAsync(), "reason")!;

        Assert.StartsWith("FDG found 1 issue(s)", reason);
        Assert.Contains("src/中文 App/Order.cs:4 CA1707", reason);
        Assert.Equal(["Order.cs"], Assert.Single(_formatter.Calls).IncludeFiles);
    }

    [Fact]
    public async Task Commits_pulled_during_the_turn_are_not_checked_and_say_so()
    {
        using var remote = new GitTestRepo(init: false);
        remote.Git("init", "-q", "--bare", "-b", "main");
        _repo.Git("remote", "add", "origin", remote.Root);
        _repo.Git("push", "-q", "-u", "origin", "main");
        using var other = new GitTestRepo(init: false);
        other.Git("clone", "-q", remote.Root, ".");
        other.Git("config", "user.name", "Someone Else");
        other.Git("config", "user.email", "else@example.invalid");
        other.Write("src/中文 App/Order.cs", Lines(10, (4, "int Bad_Name;")));
        other.CommitAll("pushed before the turn", DateTimeOffset.UtcNow.AddHours(-1));
        other.Git("push", "-q", "origin", "main");
        await TurnStartAsync();
        _repo.Git("pull", "-q", "--ff-only");
        string order = _repo.Path("src/中文 App/Order.cs");
        _builder.Sarif = _ => [new SarifResult("CA1707", "warning", "Remove the underscores", order, 4)];

        string output = await TurnStopAsync();

        Assert.Equal(
            "FDG: HEAD moved during the turn by commits not made in it; their files were not checked.",
            Json(output, "systemMessage"));
        Assert.Empty(_formatter.Calls);
        Assert.Empty(_builder.Calls);
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

        // The error in the unchanged file is not reported, but the failed build is not passed over either.
        Assert.Equal(
            "FDG found 1 issue(s) in lines you changed this turn. Fix them before finishing.\n"
            + "src/中文 App/Order.cs:9 CS0103 The name 'x' does not exist in the current context\n"
            + "    // line 9\n"
            + "FDG: dotnet build failed for src/中文 App/App.csproj (exit code 1): 1 compile error(s) in files not "
            + "changed this turn, first: src/中文 App/Other.cs:1 CS0246 In a file the AI did not change\n",
            Json(output, "reason")!);
    }

    [Fact]
    public async Task Build_failure_without_compile_errors_reports_one_line()
    {
        await TurnStartAsync();
        _repo.Write("src/中文 App/Order.cs", Lines(10, (4, "int x;")));
        _builder.Result = new ToolResult(1, "CSC : error CS2012: Cannot open 'App.dll' for writing\n", false);

        string output = await TurnStopAsync();

        Assert.Equal(
            "FDG: dotnet build failed for src/中文 App/App.csproj (exit code 1): CSC : error CS2012: Cannot open 'App.dll' for writing",
            Json(output, "systemMessage")!);
    }

    [Fact]
    public async Task Formatter_changes_on_lines_the_AI_did_not_touch_are_not_changed_lines()
    {
        _repo.Write("src/中文 App/Order.cs", Lines(10, (5, "if (x) return;")));
        _repo.CommitAll();
        await TurnStartAsync();
        string order = _repo.Write("src/中文 App/Order.cs", Lines(10, (3, "int A_1; int B_2;"), (5, "if (x) return;"), (8, "int C_3;")));
        // The whole file is formatted: the AI's line 3 is split, the old line 5 gets braces, the AI's line 8 moves to 12.
        _formatter.OnFormat = _ => File.WriteAllText(
            order,
            Lines(10, (3, "int A_1;\nint B_2;"), (5, "if (x)\n{\n    return;\n}"), (8, "int C_3;")));
        _builder.Sarif = _ => [
            new SarifResult("CA1707", "warning", "Remove the underscores B", order, 4),
            new SarifResult("IDE1006", "warning", "Naming rule violation on the old line", order, 6),
            new SarifResult("CA1707", "warning", "Remove the underscores C", order, 12),
        ];

        string reason = Json(await TurnStopAsync(), "reason")!;

        Assert.StartsWith("FDG found 2 issue(s)", reason);
        Assert.Contains("Order.cs:4 CA1707", reason);
        Assert.Contains("Order.cs:12 CA1707", reason);
        Assert.DoesNotContain("IDE1006", reason);
    }

    [Fact]
    public async Task Later_stop_does_not_count_earlier_formatting_as_the_AIs_lines()
    {
        _repo.Write("src/中文 App/Order.cs", Lines(10, (5, "if (x) return;")));
        _repo.CommitAll();
        await TurnStartAsync();
        string order = _repo.Write("src/中文 App/Order.cs", Lines(10, (5, "if (x) return;"), (8, "int Bad_Name;")));
        // Every format adds braces to the old line 5, which moves the AI's line 8 to 11.
        _formatter.OnFormat = _ => File.WriteAllText(
            order,
            File.ReadAllText(order).Replace("if (x) return;\n", "if (x)\n{\n    return;\n}\n", StringComparison.Ordinal));
        _builder.Sarif = _ => [new SarifResult("CA1707", "warning", "Remove the underscores", order, 11)];
        Assert.Equal("block", Json(await TurnStopAsync(), "decision"));

        // The AI changes only line 2 of the formatted file; the braces from the first Stop stay the formatter's.
        File.WriteAllText(order, File.ReadAllText(order).Replace("// line 2\n", "int Other_Name;\n", StringComparison.Ordinal));
        _builder.Sarif = _ => [
            new SarifResult("CA1707", "warning", "Remove the underscores", order, 2),
            new SarifResult("IDE1006", "warning", "Naming rule violation on the old line", order, 6),
            new SarifResult("CA1707", "warning", "Remove the underscores", order, 11),
        ];

        string reason = Json(await TurnStopAsync(stopHookActive: true), "reason")!;

        Assert.StartsWith("FDG found 2 issue(s)", reason);
        Assert.Contains("Order.cs:2 CA1707", reason);
        Assert.Contains("Order.cs:11 CA1707", reason);
        Assert.DoesNotContain("IDE1006", reason);
    }

    [Fact]
    public async Task Turn_start_clears_the_formatted_copies_and_AI_lines_of_the_last_turn()
    {
        await TurnStartAsync();
        _repo.Write("src/中文 App/Order.cs", Lines(10, (4, "int x;")));
        await TurnStopAsync();
        StateStore state = await StateAsync();
        Assert.Equal([4], state.ReadAiLines()["src/中文 App/Order.cs"]);
        Assert.True(File.Exists(state.FormattedPath("src/中文 App/Order.cs")));

        await TurnStartAsync();

        Assert.Empty(state.ReadAiLines());
        Assert.False(File.Exists(state.FormattedPath("src/中文 App/Order.cs")));
    }

    [Fact]
    public async Task File_changed_again_after_a_revert_is_compared_with_the_baseline()
    {
        await TurnStartAsync();
        string order = _repo.Write("src/中文 App/Order.cs", Lines(10, (4, "int Bad_Name;")));
        _builder.Sarif = _ => [new SarifResult("CA1707", "warning", "Remove the underscores", order, 4)];
        Assert.Equal("block", Json(await TurnStopAsync(), "decision"));

        // Reverted: this Stop processes no file, so it drops what the first one kept.
        _repo.Write("src/中文 App/Order.cs", Lines(10));
        Assert.Equal(string.Empty, await TurnStopAsync());
        StateStore state = await StateAsync();
        Assert.Empty(state.ReadAiLines());
        Assert.False(File.Exists(state.FormattedPath("src/中文 App/Order.cs")));

        // Line 4 is as at the start of the turn again, so only line 7 is the AI's.
        _repo.Write("src/中文 App/Order.cs", Lines(10, (7, "int Other_Name;")));
        _builder.Sarif = _ => [
            new SarifResult("CA1707", "warning", "Remove the underscores", order, 4),
            new SarifResult("CA1707", "warning", "Remove the underscores", order, 7),
        ];

        string reason = Json(await TurnStopAsync(), "reason")!;

        Assert.StartsWith("FDG found 1 issue(s)", reason);
        Assert.Contains("Order.cs:7 CA1707", reason);
    }

    [Fact]
    public async Task Stop_keeps_the_AI_lines_and_formatted_copies_of_the_files_it_processed_only()
    {
        string other = _repo.Write("src/中文 App/Other.cs", Lines(5));
        _repo.CommitAll();
        await TurnStartAsync();
        _repo.Write("src/中文 App/Order.cs", Lines(10, (4, "int x;")));
        _repo.Write("src/中文 App/Other.cs", Lines(5, (2, "int y;")));
        await TurnStopAsync();
        File.WriteAllText(other, Lines(5));

        await TurnStopAsync();

        StateStore state = await StateAsync();
        Assert.Equal(["src/中文 App/Order.cs"], state.ReadAiLines().Keys);
        Assert.True(File.Exists(state.FormattedPath("src/中文 App/Order.cs")));
        Assert.False(File.Exists(state.FormattedPath("src/中文 App/Other.cs")));
    }

    [Fact]
    public async Task Stop_without_the_stop_hook_active_field_keeps_the_block_count()
    {
        await TurnStartAsync();
        string order = _repo.Write("src/中文 App/Order.cs", Lines(10, (4, "int Bad_Name;")));
        _builder.Sarif = _ => [new SarifResult("CA1707", "warning", "Remove the underscores", order, 4)];
        await TurnStopAsync(stopHookActive: null);
        await TurnStopAsync(stopHookActive: null);

        string third = await TurnStopAsync(stopHookActive: null);

        Assert.Null(Json(third, "decision"));
        Assert.StartsWith("FDG found 1 issue(s)", Json(third, "systemMessage")!);
    }

    [Fact]
    public async Task Files_moved_from_several_deleted_files_each_find_their_own()
    {
        _repo.Write("src/中文 App/A.cs", Lines(10, (4, "int A_Old;")).Replace("// line", "// a", StringComparison.Ordinal));
        _repo.Write("src/中文 App/B.cs", Lines(10, (4, "int B_Old;")).Replace("// line", "// b", StringComparison.Ordinal));
        _repo.CommitAll();
        await TurnStartAsync();
        foreach (string name in (string[])["A", "B"])
        {
            File.Move(_repo.Path($"src/中文 App/{name}.cs"), _repo.Path($"src/中文 App/{name}Moved.cs"));
        }

        string a = _repo.Path("src/中文 App/AMoved.cs");
        string b = _repo.Path("src/中文 App/BMoved.cs");
        _builder.Sarif = _ => [
            new SarifResult("CA1707", "warning", "Remove the underscores", a, 4),
            new SarifResult("CA1707", "warning", "Remove the underscores", b, 4),
        ];

        Assert.Equal(string.Empty, await TurnStopAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Moved_file_takes_the_deleted_file_as_baseline(bool gitMv)
    {
        _repo.Write("src/中文 App/Old.cs", Lines(10, (4, "int Old_Name;")));
        _repo.CommitAll();
        await TurnStartAsync();
        Directory.CreateDirectory(_repo.Path("src/中文 App/Moved"));
        if (gitMv)
        {
            _repo.Git("mv", "src/中文 App/Old.cs", "src/中文 App/Moved/New.cs");
        }
        else
        {
            File.Move(_repo.Path("src/中文 App/Old.cs"), _repo.Path("src/中文 App/Moved/New.cs"));
        }

        string moved = _repo.Write("src/中文 App/Moved/New.cs", Lines(10, (4, "int Old_Name;"), (9, "int New_Name;")));
        _builder.Sarif = _ => [
            new SarifResult("CA1707", "warning", "Remove the underscores", moved, 4),
            new SarifResult("CA1707", "warning", "Remove the underscores", moved, 9),
        ];

        string reason = Json(await TurnStopAsync(), "reason")!;

        Assert.StartsWith("FDG found 1 issue(s)", reason);
        Assert.Contains("src/中文 App/Moved/New.cs:9 CA1707", reason);
    }

    [Fact]
    public async Task Stop_without_stop_hook_active_starts_a_new_block_count()
    {
        // The host continues after a block with stop_hook_active; without it, the user prompted again even if
        // UserPromptSubmit never reset the count.
        await TurnStartAsync();
        string order = _repo.Write("src/中文 App/Order.cs", Lines(10, (4, "int Bad_Name;")));
        _builder.Sarif = _ => [new SarifResult("CA1707", "warning", "Remove the underscores", order, 4)];
        await TurnStopAsync();
        await TurnStopAsync(stopHookActive: true);

        string output = await TurnStopAsync();

        Assert.Equal("block", Json(output, "decision"));
    }

    [Fact]
    public async Task Turn_reset_removes_the_old_snapshot_first()
    {
        await TurnStartAsync();
        StateStore state = await StateAsync();

        state.ResetTurn();

        Assert.Null(state.ReadSnapshot());
    }

    [Fact]
    public async Task Session_start_outside_git_looks_two_levels_deep_only()
    {
        using var tree = new TempTree();
        var input = new HookInput(HostKind.Claude, Session, tree.Root, "SessionStart");
        tree.Write("a/b/c/Deep.csproj", OnboardedProject);
        tree.Write("a/bin/Built.csproj", OnboardedProject);

        Assert.Equal(string.Empty, await CreateRunner().SessionStartAsync(input, CancellationToken.None));
        tree.Write("a/b/Shallow.csproj", OnboardedProject);
        Assert.Contains(ChecksDisabled, Json(await CreateRunner().SessionStartAsync(input, CancellationToken.None), "systemMessage"));
    }

    [Fact]
    public async Task Session_start_outside_git_does_not_follow_junctions()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var tree = new TempTree();
        using var target = new TempTree();
        target.Write("App/App.csproj", OnboardedProject);
        // Like "Application Data" in a user profile: a junction, often one that denies listing.
        string link = tree.Path("Application Data");
        var startInfo = new System.Diagnostics.ProcessStartInfo("cmd") { RedirectStandardOutput = true, UseShellExecute = false };
        foreach (string argument in (string[])["/c", "mklink", "/J", link, target.Root])
        {
            startInfo.ArgumentList.Add(argument);
        }

        using (System.Diagnostics.Process mklink = System.Diagnostics.Process.Start(startInfo)!)
        {
            await mklink.WaitForExitAsync();
        }

        Assert.True(File.Exists(Path.Combine(link, "App", "App.csproj")));
        var input = new HookInput(HostKind.Claude, Session, tree.Root, "SessionStart");

        try
        {
            Assert.Equal(string.Empty, await CreateRunner().SessionStartAsync(input, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(link);
        }
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
    public async Task Breaking_diagnostic_on_existing_api_does_not_block()
    {
        CommitOrder("class Order {\n    int count;\n    void Ship() { }\n}\n");
        await TurnStartAsync();
        string order = _repo.Write("src/中文 App/Order.cs", "class Order {\n    private int count;\n    void Ship() { }\n}\n");
        _builder.Sarif = _ => [new SarifResult("IDE1006", "warning", "Naming rule violation: Missing prefix: '_'", order, 2) { Column = 17 }];

        string output = await TurnStopAsync();

        Assert.Null(Json(output, "decision"));
        string message = Json(output, "systemMessage")!;
        Assert.StartsWith(
            "FDG: 1 issue(s) on existing API in lines you changed; not blocking. Tell the user instead of changing the API.\n" +
            "Existing API: do not change it to fix this; tell the user.\n" +
            "src/中文 App/Order.cs:2 IDE1006 Naming rule violation: Missing prefix: '_'\n",
            message);
        Assert.Equal(0, (await StateAsync()).ReadBlocks());
    }

    [Fact]
    public async Task Existing_api_issues_are_listed_after_blocking_ones()
    {
        CommitOrder("class Order {\n    int count;\n    void Ship() { }\n}\n");
        await TurnStartAsync();
        string order = _repo.Write(
            "src/中文 App/Order.cs",
            "class Order {\n    private int count;\n    void Ship() { }\n    private int total;\n}\n");
        _builder.Sarif = _ => [
            new SarifResult("IDE1006", "warning", "Naming rule violation: Missing prefix: '_'", order, 2) { Column = 17 },
            new SarifResult("IDE1006", "warning", "Naming rule violation: Missing prefix: '_'", order, 4) { Column = 17 },
        ];

        string output = await TurnStopAsync();

        Assert.Equal("block", Json(output, "decision"));
        string reason = Json(output, "reason")!;
        Assert.StartsWith("FDG found 1 issue(s) in lines you changed this turn. Fix them before finishing.\n", reason);
        int created = reason.IndexOf("src/中文 App/Order.cs:4 IDE1006", StringComparison.Ordinal);
        int marker = reason.IndexOf("Existing API: do not change it to fix this; tell the user.\n", StringComparison.Ordinal);
        int existing = reason.IndexOf("src/中文 App/Order.cs:2 IDE1006", StringComparison.Ordinal);
        Assert.InRange(created, 1, marker - 1);
        Assert.InRange(marker, created + 1, existing - 1);
    }

    [Fact]
    public async Task Non_breaking_diagnostic_on_existing_member_still_blocks()
    {
        CommitOrder("class Order {\n    int count;\n    void Ship() { }\n}\n");
        await TurnStartAsync();
        string order = _repo.Write("src/中文 App/Order.cs", "class Order {\n    private int count;\n    void Ship() { }\n}\n");
        _builder.Sarif = _ => [new SarifResult("IDE0044", "warning", "Make field readonly", order, 2) { Column = 17 }];

        string output = await TurnStopAsync();

        Assert.Equal("block", Json(output, "decision"));
        string reason = Json(output, "reason")!;
        Assert.StartsWith("FDG found 1 issue(s) in lines you changed this turn. Fix them before finishing.\n", reason);
        Assert.DoesNotContain("Existing API", reason);
    }

    [Fact]
    public async Task Breaking_diagnostic_on_a_new_overload_of_an_existing_method_blocks()
    {
        CommitOrder("class Order {\n    void Ship(int x) { }\n}\n");
        await TurnStartAsync();
        string order = _repo.Write(
            "src/中文 App/Order.cs",
            "class Order {\n    void Ship(int x) { }\n    void Ship(string Name) { }\n}\n");
        _builder.Sarif = _ => [new SarifResult("IDE1006", "warning", "Naming rule violation: These words must begin with upper case characters: Name", order, 3) { Column = 23 }];

        string output = await TurnStopAsync();

        Assert.Equal("block", Json(output, "decision"));
    }

    [Fact]
    public async Task Breaking_diagnostic_in_a_file_created_this_turn_blocks()
    {
        await TurnStartAsync();
        string created = _repo.Write("src/中文 App/Created.cs", "class Created {\n    int count;\n}\n");
        _builder.Sarif = _ => [new SarifResult("IDE1006", "warning", "Naming rule violation: Missing prefix: '_'", created, 2) { Column = 9 }];

        Assert.Equal("block", Json(await TurnStopAsync(), "decision"));
    }

    [Fact]
    public async Task Existing_api_issues_count_against_the_20_issue_limit()
    {
        CommitOrder(string.Concat(Enumerable.Range(1, 15).Select(i => $"class C{i} {{ }}\n")));
        await TurnStartAsync();
        string order = _repo.Write(
            "src/中文 App/Order.cs",
            string.Concat(Enumerable.Range(1, 15).Select(i => $"class C{i} {{ }} // x\n")) +
            string.Concat(Enumerable.Range(16, 10).Select(i => $"class C{i} {{ }}\n")));
        _builder.Sarif = _ => Enumerable.Range(1, 25)
            .Select(i => new SarifResult("CA1707", "warning", $"Remove the underscores {i}", order, i) { Column = 7 })
            .ToArray();

        string reason = Json(await TurnStopAsync(), "reason")!;

        string[] listed = reason.Split('\n').Where(l => l.StartsWith("src/", StringComparison.Ordinal)).ToArray();
        Assert.Equal(20, listed.Length);
        Assert.StartsWith("FDG found 10 issue(s)", reason);
        // The 10 new classes (lines 16-25) come first, then the existing ones (lines 1-10 of 15) that still fit.
        Assert.Equal(Enumerable.Range(16, 10), listed.Take(10).Select(LineOf));
        Assert.Equal(Enumerable.Range(1, 10), listed.Skip(10).Select(LineOf));
        Assert.Contains("\nExisting API: do not change it to fix this; tell the user.\n", reason);
        Assert.EndsWith("\n... and 5 more.\n", reason);
    }

    [Fact]
    public async Task Third_stop_in_same_turn_allows_finish_with_system_message()
    {
        await TurnStartAsync();
        string order = _repo.Write("src/中文 App/Order.cs", Lines(10, (4, "int Bad_Name;")));
        _builder.Sarif = _ => [new SarifResult("CA1707", "warning", "Remove the underscores", order, 4)];

        string first = await TurnStopAsync();
        string second = await TurnStopAsync(stopHookActive: true);
        string third = await TurnStopAsync(stopHookActive: true);

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
    public async Task Referenced_project_log_in_the_project_directory_triggers_no_incremental_rebuild()
    {
        string lib = _repo.Write("src/Lib/Lib.csproj", Project);
        string libFile = _repo.Write("src/Lib/L.cs", Lines(5));
        _repo.CommitAll();
        await TurnStartAsync();
        string order = _repo.Write("src/中文 App/Order.cs", Lines(10, (4, "int Bad_Name;")));
        // The up-to-date entry project skipped CoreCompile; the log in its directory is the referenced project's.
        _builder.Sarif = call => call.NoIncremental
            ? [new SarifResult("CA1707", "warning", "Remove the underscores", order, 4)]
            : [new SarifResult("CA1707", "warning", "In the referenced project", libFile, 2)];

        string output = await TurnStopAsync();

        Assert.Equal([(_project, false), (_project, true)], _builder.Calls.Select(c => (c.ProjectPath, c.NoIncremental)));
        Assert.Contains("Order.cs:4 CA1707", Json(output, "reason")!);
        Assert.DoesNotContain(lib, _builder.Calls.Select(c => c.ProjectPath));
    }

    [Fact]
    public async Task At_most_4_projects_are_formatted_at_once()
    {
        for (int i = 0; i < 7; i++)
        {
            _projects.Add(_repo.Write($"src/P{i}/P{i}.csproj", OnboardedProject), ProjectState.InScope);
        }

        _repo.CommitAll();
        await TurnStartAsync();
        for (int i = 0; i < 7; i++)
        {
            _repo.Write($"src/P{i}/C.cs", Lines(3));
        }

        _formatter.Delay = TimeSpan.FromMilliseconds(300);

        await TurnStopAsync();

        Assert.Equal(7, _formatter.Calls.Count);
        Assert.InRange(_formatter.MaxConcurrent, 2, 4);
    }

    [Fact]
    public async Task Projects_are_built_one_at_a_time()
    {
        string lib = _repo.Write("src/Lib/Lib.csproj", OnboardedProject);
        _projects.Add(lib, ProjectState.InScope);
        _repo.CommitAll();
        await TurnStartAsync();
        _repo.Write("src/中文 App/Order.cs", Lines(10, (4, "int x;")));
        _repo.Write("src/Lib/L.cs", Lines(5));
        _builder.Delay = TimeSpan.FromMilliseconds(300);

        await TurnStopAsync();

        Assert.Equal(2, _builder.Calls.Count);
        Assert.Equal(1, _builder.MaxConcurrent);
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
    public async Task Build_that_only_failed_to_copy_its_output_says_the_diagnostics_were_checked()
    {
        await TurnStartAsync();
        string order = _repo.Write("src/中文 App/Order.cs", Lines(10, (4, "int Bad_Name;")));
        // As measured with the app running: the copy to bin/ fails, the compile step wrote the log.
        _builder.Result = new ToolResult(
            1,
            "warning MSB3026: Could not copy \"obj/App.dll\" to \"bin/App.dll\". Beginning retry 1 in 1000ms.\n"
            + "error MSB3027: Could not copy \"obj/App.dll\" to \"bin/App.dll\". Exceeded retry count of 0. Failed.\n"
            + "error MSB3021: Unable to copy file \"obj/App.dll\" to \"bin/App.dll\". The process cannot access the file.\n",
            false);
        _builder.Sarif = _ => [];
        const string Note = "FDG: output copy failed (is the app running?); diagnostics were checked.";

        Assert.Equal(Note, Json(await TurnStopAsync(), "systemMessage"));

        _builder.Sarif = _ => [new SarifResult("CA1707", "warning", "Remove the underscores", order, 4)];
        string reason = Json(await TurnStopAsync(), "reason")!;
        Assert.Contains("Order.cs:4 CA1707", reason);
        Assert.EndsWith("\n" + Note + "\n", reason);

        // Any other error is a failed build, including one without a code, such as a custom Error task's.
        ToolResult copyFailure = _builder.Result;
        _builder.Result = copyFailure with { Output = copyFailure.Output + "error MSB4018: The task failed.\n" };
        Assert.StartsWith("FDG: dotnet build failed", Json(await TurnStopAsync(stopHookActive: true), "reason")!.Split('\n')[^2]);
        _builder.Result = copyFailure with { Output = copyFailure.Output + "App.csproj(12,5): error : Custom check failed.\n" };
        Assert.StartsWith("FDG: dotnet build failed", Json(await TurnStopAsync(), "reason")!.Split('\n')[^2]);
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
            "FDG: the Stop check timed out after 1 s (FDG_HOOK_TIMEOUT_SECONDS); this turn was not checked, "
            + "and files may have been partially reformatted.",
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
            "FDG auto-formatted at the end of the last turn: src/中文 App/Order.cs (line 8). Re-read those regions before editing them.",
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

    [Theory]
    [InlineData(null)]
    [InlineData(Project)]
    public async Task Turn_hooks_do_nothing_in_a_repository_without_onboarded_projects(string? projectXml)
    {
        using var repo = new GitTestRepo();
        repo.Write("README.md", "Uses FrameworkDesignGuidelines.Analyzers? Not yet.\n");
        if (projectXml is not null)
        {
            _projects.Add(repo.Write("App/App.csproj", projectXml), ProjectState.InScope);
            repo.Write("App/A.cs", "class A { }\n");
        }

        repo.CommitAll();
        repo.Write("App/A.cs", "class A { int x; }\n");
        HookRunner runner = CreateRunner();
        var input = new HookInput(HostKind.Claude, Session, repo.Root, "UserPromptSubmit");

        Assert.Equal(string.Empty, await runner.TurnStartAsync(input, CancellationToken.None));
        repo.Write("App/A.cs", "class A { int y; }\n");
        // Turn-start took no snapshot, and Stop does not say so.
        Assert.Equal(string.Empty, await runner.TurnStopAsync(input with { EventName = "Stop" }, CancellationToken.None));
        Assert.False(Directory.Exists(repo.Path(".git/fdg")));
        Assert.Empty(_formatter.Calls);
        Assert.Empty(_builder.Calls);
    }

    [Fact]
    public async Task Session_start_removes_state_of_sessions_older_than_7_days_but_never_the_current_one()
    {
        string fdg = _repo.Path(".git/fdg");
        string old = Directory.CreateDirectory(Path.Combine(fdg, "old-session")).FullName;
        File.WriteAllText(Path.Combine(old, "snapshot.json"), "{}");
        string recent = Directory.CreateDirectory(Path.Combine(fdg, "recent-session")).FullName;
        string current = Directory.CreateDirectory(Path.Combine(fdg, StateStore.SanitizeSessionId(Session))).FullName;
        Directory.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-8));
        Directory.SetLastWriteTimeUtc(recent, DateTime.UtcNow.AddDays(-6));
        Directory.SetLastWriteTimeUtc(current, DateTime.UtcNow.AddDays(-30));

        await CreateRunner().SessionStartAsync(new HookInput(HostKind.Claude, Session, _repo.Root, "SessionStart"), CancellationToken.None);

        Assert.False(Directory.Exists(old));
        Assert.True(Directory.Exists(recent));
        Assert.True(Directory.Exists(current));
    }

    [Fact]
    public async Task Session_start_still_gives_its_context_when_the_old_state_cannot_be_listed()
    {
        string fdg = Directory.CreateDirectory(_repo.Path(".git/fdg")).FullName;
        Directory.CreateDirectory(Path.Combine(fdg, "old-session"));
        DenyListing(fdg, deny: true);
        try
        {
            string output = await CreateRunner().SessionStartAsync(
                new HookInput(HostKind.Claude, Session, _repo.Root, "SessionStart"),
                CancellationToken.None);

            Assert.NotEqual(string.Empty, output);
        }
        finally
        {
            DenyListing(fdg, deny: false);
        }
    }

    [Fact]
    public async Task Session_start_outside_git_says_checks_are_disabled_for_onboarded_projects_only()
    {
        using var tree = new TempTree();
        var input = new HookInput(HostKind.Codex, Session, tree.Root, "SessionStart");

        tree.Write("App/App.csproj", Project);
        Assert.Equal(string.Empty, await CreateRunner().SessionStartAsync(input, CancellationToken.None));
        tree.Write("Lib/Lib.csproj", OnboardedProject);
        Assert.Contains(
            "FDG setup issue: " + ChecksDisabled,
            Json(await CreateRunner().SessionStartAsync(input, CancellationToken.None), "systemMessage"));
        Assert.DoesNotContain(
            ChecksDisabled,
            await CreateRunner().SessionStartAsync(input with { Cwd = _repo.Root }, CancellationToken.None));
    }

    [Fact]
    public async Task Turn_start_resets_block_counter()
    {
        await TurnStartAsync();
        string order = _repo.Write("src/中文 App/Order.cs", Lines(10, (4, "int Bad_Name;")));
        _builder.Sarif = _ => [new SarifResult("CA1707", "warning", "Remove the underscores", order, 4)];
        await TurnStopAsync();
        await TurnStopAsync(stopHookActive: true);

        await TurnStartAsync();
        _repo.Write("src/中文 App/Order.cs", Lines(10, (4, "int Bad_Name;"), (5, "int Other_Name;")));
        _builder.Sarif = _ => [new SarifResult("CA1707", "warning", "Remove the underscores", order, 5)];
        string output = await TurnStopAsync(stopHookActive: true);

        Assert.Equal("block", Json(output, "decision"));
    }

    [Fact]
    public async Task Missing_snapshot_reports_one_line()
    {
        // As when the host ends turn-start between removing the old snapshot and writing the new one.
        await TurnStartAsync();
        File.Delete(Path.Combine((await StateAsync()).Directory, "snapshot.json"));
        _repo.Write("src/中文 App/Order.cs", Lines(10, (4, "int x;")));

        string output = await TurnStopAsync();

        Assert.Equal(
            "FDG: no snapshot from the start of this turn; the Stop check was skipped.",
            Json(output, "systemMessage")!);
    }

    [Fact]
    public async Task Stop_of_a_session_without_state_says_nothing()
    {
        // The turn that ran /fdg-setup: the repository was not onboarded when the turn started.
        _repo.Write("src/中文 App/Order.cs", Lines(10, (4, "int x;")));

        Assert.Equal(string.Empty, await TurnStopAsync());
        Assert.Empty(_formatter.Calls);
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
    public async Task Hook_command_turns_a_failed_stdin_read_into_a_system_message()
    {
        var stdout = new StringWriter();

        int exitCode = await HookCommands.RunAsync(
            "turn-stop",
            new FailingReader(),
            stdout,
            new StringWriter(),
            CreateRunner(),
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal("FDG hook error: The pipe has been ended.", Json(stdout.ToString(), "systemMessage")!);
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

    private void CommitOrder(string content)
    {
        _repo.Write("src/中文 App/Order.cs", content);
        _repo.CommitAll();
    }

    private static int LineOf(string issue) => int.Parse(issue.Split(':')[1].Split(' ')[0]);

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
        new(
            _projects,
            _formatter,
            _builder,
            new SessionContext(new FakeProcessRunner().Respond(args => args is ["--version"], new ToolResult(0, "10.0.100\n", false))),
            name => _environment.GetValueOrDefault(name));

    private Task<string> TurnStartAsync(HostKind host = HostKind.Claude) =>
        CreateRunner().TurnStartAsync(new HookInput(host, Session, _repo.Root, "UserPromptSubmit"), CancellationToken.None);

    private Task<string> TurnStopAsync(bool? stopHookActive = false) =>
        CreateRunner().TurnStopAsync(
            new HookInput(HostKind.Claude, Session, _repo.Root, "Stop") { StopHookActive = stopHookActive },
            CancellationToken.None);

    private sealed class FailingReader : TextReader
    {
        public override Task<string> ReadToEndAsync(CancellationToken cancellationToken) =>
            throw new IOException("The pipe has been ended.");
    }

    /// <summary>Makes <paramref name="directory"/> unlistable for the current user, or listable again.</summary>
    private static void DenyListing(string directory, bool deny)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                directory,
                deny ? UnixFileMode.None : UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            return;
        }

        string user = Environment.UserDomainName + "\\" + Environment.UserName;
        var startInfo = new System.Diagnostics.ProcessStartInfo("icacls") { UseShellExecute = false, RedirectStandardOutput = true };
        foreach (string argument in deny ? [directory, "/deny", user + ":(RD)"] : (string[])[directory, "/remove:d", user])
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = System.Diagnostics.Process.Start(startInfo)!;
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }

    private async Task<StateStore> StateAsync() =>
        StateStore.For((await GitRepository.OpenAsync(_repo.Root, CancellationToken.None))!, Session);
}
