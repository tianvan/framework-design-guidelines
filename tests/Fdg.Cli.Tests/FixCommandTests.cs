using Fdg.Cli.Commands;
using Fdg.Cli.Projects;
using Fdg.Cli.Tooling;

namespace Fdg.Cli.Tests;

public sealed class FixCommandTests : IDisposable
{
    private readonly TempTree _tree = new();
    private readonly FakeProjects _projects = new();
    private readonly FakeRestoreRunner _restorer = new();
    private readonly FakeFormatRunner _formatter = new();
    private readonly StringWriter _output = new();

    public void Dispose()
    {
        _tree.Dispose();
    }

    [Fact]
    public async Task Whole_project_is_formatted_and_reported_relative_to_the_current_directory()
    {
        string project = AddProject("src/中文 目录/App/App.csproj", ProjectState.InScope);

        int exitCode = await RunAsync();

        Assert.Equal(0, exitCode);
        FormatCall call = Assert.Single(_formatter.Calls);
        Assert.Equal(project, call.ProjectPath);
        Assert.Null(call.IncludeFiles);
        Assert.Equal("fixed src/中文 目录/App/App.csproj\n", Output);
    }

    [Fact]
    public async Task Paths_with_spaces_and_cjk_characters_are_passed_intact()
    {
        AddProject("App.csproj", ProjectState.InScope);
        _tree.Write("src/中文 目录/A.cs", "class A { }");
        _tree.Write("src/中文 目录/深 层/B.cs", "class B { }");

        int exitCode = await RunAsync("src/中文 目录/A.cs", Path.Combine(_tree.Root, "src", "中文 目录", "深 层"));

        Assert.Equal(0, exitCode);
        FormatCall call = Assert.Single(_formatter.Calls);
        // dotnet format matches --include against paths relative to its working directory, the project directory.
        Assert.Equal(["src/中文 目录/A.cs", "src/中文 目录/深 层/"], call.IncludeFiles);
    }

    [Fact]
    public async Task Out_of_scope_projects_are_skipped_with_reason()
    {
        AddProject("Legacy/Legacy.csproj", ProjectState.NotSdkStyle);
        AddProject("Plain/Plain.csproj", ProjectState.NoPackage);
        AddProject("Unset/Unset.csproj", ProjectState.NoProfile);
        AddProject("Typo/Typo.csproj", ProjectState.NoProfile, profile: "libary");

        int exitCode = await RunAsync();

        Assert.Equal(0, exitCode);
        Assert.Empty(_formatter.Calls);
        Assert.Equal(
            "skipped Legacy/Legacy.csproj: not an SDK-style project\n"
            + "skipped Plain/Plain.csproj: FrameworkDesignGuidelines.Analyzers is not referenced\n"
            + "skipped Typo/Typo.csproj: unknown FrameworkDesignGuidelinesProfile 'libary'\n"
            + "skipped Unset/Unset.csproj: FrameworkDesignGuidelinesProfile is not set\n",
            Output);
    }

    [Fact]
    public async Task Unreadable_project_is_skipped_as_a_failure_and_the_others_are_fixed()
    {
        AddProject("A/A.csproj", ProjectState.InScope);
        string locked = _tree.Write("B/B.csproj", TempTree.SdkProject);
        _projects.Throw(locked, new UnauthorizedAccessException("Access to the path is denied."));

        int exitCode = await RunAsync();

        Assert.Equal(1, exitCode);
        Assert.Equal(
            "fixed A/A.csproj\n"
            + "skipped B/B.csproj: cannot read the project file: Access to the path is denied.\n",
            Output);
    }

    [Fact]
    public async Task Unreadable_project_with_the_real_provider_is_skipped()
    {
        string project = _tree.Write("A/A.csproj", TempTree.SdkProject);
        var command = new FixCommand(
            new ProjectInfoProvider(new FakeProcessRunner(), new CompilerLanguageVersions(new FakeProcessRunner())),
            _restorer,
            _formatter,
            _output,
            _output);
        int exitCode;
        using (new FileStream(project, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            exitCode = await command.RunAsync([], _tree.Root, CancellationToken.None);
        }

        Assert.Equal(1, exitCode);
        Assert.StartsWith("skipped A/A.csproj: cannot read the project file: ", Output);
    }

    [Fact]
    public async Task Newer_package_protocol_is_skipped_as_a_failure()
    {
        AddProject("A/A.csproj", ProjectState.InScope, protocol: 2);

        int exitCode = await RunAsync();

        Assert.Equal(1, exitCode);
        Assert.Empty(_formatter.Calls);
        Assert.StartsWith("skipped A/A.csproj: package protocol 2 is newer than this dotnet-fdg supports (1)", Output);
    }

    [Fact]
    public async Task Breaking_fix_diagnostics_are_excluded()
    {
        string rules = _tree.Write(
            "packages/rules.json",
            """
            { "protocol": 1, "version": "0.1.0", "rules": [], "registeredDiagnostics": ["CA1707"],
              "breakingFixDiagnostics": ["CA1707", "IDE1006", "FDG9999"] }
            """);
        AddProject("A/A.csproj", ProjectState.InScope, rulesPath: rules);

        await RunAsync();

        Assert.Equal(["CA1707", "IDE1006", "FDG9999"], Assert.Single(_formatter.Calls).ExcludeDiagnostics);
    }

    [Fact]
    public async Task Embedded_rules_are_used_when_the_package_copy_is_unreadable()
    {
        AddProject("A/A.csproj", ProjectState.InScope, rulesPath: _tree.Path("missing/rules.json"));
        AddProject("B/B.csproj", ProjectState.InScope, rulesPath: null);

        await RunAsync();

        Assert.Equal(2, _formatter.Calls.Count);
        Assert.All(_formatter.Calls, c => Assert.Equal(["CA1707", "IDE0130", "IDE1006"], c.ExcludeDiagnostics));
    }

    [Fact]
    public async Task Format_failure_returns_exit_code_1_with_message()
    {
        AddProject("A/A.csproj", ProjectState.InScope);
        AddProject("B/B.csproj", ProjectState.InScope);
        string details = string.Join('\n', Enumerable.Range(1, 30).Select(i => "line " + i));
        _formatter.Result = path => path.EndsWith("A.csproj", StringComparison.Ordinal)
            ? new ToolResult(1, details, false)
            : new ToolResult(0, string.Empty, false);

        int exitCode = await RunAsync();

        Assert.Equal(1, exitCode);
        string[] lines = Output.Split('\n');
        Assert.Equal("failed A/A.csproj: dotnet format exited with code 1", lines[0]);
        Assert.Equal("  line 1", lines[1]);
        Assert.Equal("  line 20", lines[20]);
        Assert.DoesNotContain("  line 21", lines);
        // The other project is still processed.
        Assert.Contains("fixed B/B.csproj", lines);
    }

    [Fact]
    public async Task Format_timeout_is_a_failure()
    {
        AddProject("A/A.csproj", ProjectState.InScope);
        _formatter.Result = _ => new ToolResult(-1, string.Empty, true);

        Assert.Equal(1, await RunAsync());
        Assert.StartsWith("failed A/A.csproj: dotnet format timed out", Output);
    }

    [Fact]
    public async Task Format_is_repeated_while_files_change_up_to_three_passes()
    {
        AddProject("A/A.csproj", ProjectState.InScope);
        string file = _tree.Write("A/Code.cs", "0");
        _formatter.OnCall = _ => File.AppendAllText(file, "+");

        Assert.Equal(0, await RunAsync());
        Assert.Equal(3, _formatter.Calls.Count);
    }

    [Fact]
    public async Task Format_stops_once_a_pass_changes_nothing()
    {
        AddProject("A/A.csproj", ProjectState.InScope);
        string file = _tree.Write("A/Code.cs", "0");
        _formatter.OnCall = count =>
        {
            if (count == 1)
            {
                File.WriteAllText(file, "1");
            }
        };

        Assert.Equal(0, await RunAsync());
        Assert.Equal(2, _formatter.Calls.Count);
    }

    [Fact]
    public async Task Changes_outside_the_include_do_not_trigger_another_pass()
    {
        AddProject("A/A.csproj", ProjectState.InScope);
        _tree.Write("A/Included.cs", "0");
        string other = _tree.Write("A/Other.cs", "0");
        _formatter.OnCall = _ => File.AppendAllText(other, "+");

        Assert.Equal(0, await RunAsync("A/Included.cs"));
        Assert.Single(_formatter.Calls);
    }

    [Fact]
    public async Task Unrestored_project_is_restored_then_formatted()
    {
        string project = AddProject("A/A.csproj", ProjectState.NotRestored, ProjectState.InScope);

        int exitCode = await RunAsync();

        Assert.Equal(0, exitCode);
        Assert.Equal([project], _restorer.Calls);
        Assert.Single(_formatter.Calls);
        Assert.Equal("fixed A/A.csproj\n", Output);
    }

    [Fact]
    public async Task Restore_failure_skips_the_project_with_exit_code_1()
    {
        AddProject("A/A.csproj", ProjectState.NotRestored);
        _restorer.Result = new ToolResult(1, "error NU1101: Unable to find package X.\nsecond line", false);

        int exitCode = await RunAsync();

        Assert.Equal(1, exitCode);
        Assert.Empty(_formatter.Calls);
        Assert.Equal(
            "skipped A/A.csproj: restore failed\n  error NU1101: Unable to find package X.\n  second line\n",
            Output);
    }

    [Fact]
    public async Task No_projects_found_is_reported()
    {
        _tree.Write("loose/Code.cs");

        Assert.Equal(0, await RunAsync());
        Assert.Equal("no C# projects found\n", Output);
    }

    [Fact]
    public async Task Missing_path_returns_exit_code_2()
    {
        var error = new StringWriter();
        var command = new FixCommand(_projects, _restorer, _formatter, _output, error);

        Assert.Equal(2, await command.RunAsync(["missing"], _tree.Root, CancellationToken.None));
        Assert.Contains("missing", error.ToString());
    }

    private string Output => _output.ToString().ReplaceLineEndings("\n");

    private string AddProject(
        string relativePath,
        ProjectState state,
        ProjectState? stateAfterRestore = null,
        string? profile = "library",
        int protocol = 1,
        string? rulesPath = null)
    {
        string path = _tree.Write(relativePath, TempTree.SdkProject);
        string? effectiveProfile = state == ProjectState.NoProfile && profile == "library" ? null : profile;
        _projects.Add(path, new ProjectInfo(path, effectiveProfile, "0.1.0", protocol, "7.3", "netstandard2.0", rulesPath, state));
        if (stateAfterRestore is { } after)
        {
            _projects.Add(path, new ProjectInfo(path, effectiveProfile, "0.1.0", protocol, "7.3", "netstandard2.0", rulesPath, after));
        }

        return path;
    }

    private Task<int> RunAsync(params string[] paths)
    {
        var command = new FixCommand(_projects, _restorer, _formatter, _output, _output);
        return command.RunAsync(paths, _tree.Root, CancellationToken.None);
    }

    private sealed record FormatCall(
        string ProjectPath,
        IReadOnlyList<string>? IncludeFiles,
        IReadOnlyList<string> ExcludeDiagnostics);

    /// <summary>Answers with queued infos per project; the last one repeats.</summary>
    private sealed class FakeProjects : IProjectInfoProvider
    {
        private readonly Dictionary<string, Queue<ProjectInfo>> _infos = new(StringComparer.OrdinalIgnoreCase);

        public void Add(string path, ProjectInfo info)
        {
            if (!_infos.TryGetValue(path, out Queue<ProjectInfo>? queue))
            {
                queue = new Queue<ProjectInfo>();
                _infos.Add(path, queue);
            }

            queue.Enqueue(info);
        }

        private readonly Dictionary<string, Exception> _failures = new(StringComparer.OrdinalIgnoreCase);

        public void Throw(string path, Exception exception)
        {
            _failures.Add(path, exception);
        }

        public ProjectInfo Get(string csprojPath)
        {
            if (_failures.TryGetValue(csprojPath, out Exception? failure))
            {
                throw failure;
            }

            Queue<ProjectInfo> queue = _infos[csprojPath];
            return queue.Count > 1 ? queue.Dequeue() : queue.Peek();
        }
    }

    private sealed class FakeRestoreRunner : IRestoreRunner
    {
        public List<string> Calls { get; } = [];

        public ToolResult Result { get; set; } = new(0, string.Empty, false);

        public Task<ToolResult> RestoreAsync(string projectPath, CancellationToken cancellationToken)
        {
            Calls.Add(projectPath);
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeFormatRunner : IFormatRunner
    {
        public List<FormatCall> Calls { get; } = [];

        public Func<string, ToolResult> Result { get; set; } = _ => new ToolResult(0, string.Empty, false);

        /// <summary>Runs on every call with the 1-based call number, standing in for dotnet format's edits.</summary>
        public Action<int> OnCall { get; set; } = _ => { };

        public Task<ToolResult> FormatAsync(
            string projectPath,
            IReadOnlyList<string>? includeFiles,
            IReadOnlyList<string> excludeDiagnostics,
            CancellationToken cancellationToken)
        {
            Calls.Add(new FormatCall(projectPath, includeFiles, excludeDiagnostics));
            OnCall(Calls.Count);
            return Task.FromResult(Result(projectPath));
        }
    }
}
