using Fdg.Cli.Tooling;

namespace Fdg.Cli.Tests;

public sealed class DotnetBuildRunnerTests
{
    private static readonly string Project = Path.Combine(Path.GetTempPath(), "中文 目录", "App", "App.csproj");

    private static readonly string SarifDirectory = Path.Combine(Path.GetTempPath(), "sarif dir");

    [Fact]
    public async Task Build_names_the_entry_project_so_only_it_writes_a_SARIF_log()
    {
        var runner = new FakeProcessRunner().Respond(_ => true, new ToolResult(0, string.Empty, false));

        await new DotnetBuildRunner(runner, TimeSpan.FromMinutes(1))
            .BuildAsync(Project, SarifDirectory, noIncremental: false, noDependencies: false, CancellationToken.None);

        Assert.Equal(
            [
                "build", Project, "--no-restore", "-p:FdgSarifDir=" + SarifDirectory, "-p:FdgSarifProject=" + Project,
                "-p:CopyRetryCount=0",
            ],
            Assert.Single(runner.Calls));
    }

    [Fact]
    public async Task Forced_rebuild_recompiles_only_the_project()
    {
        var runner = new FakeProcessRunner().Respond(_ => true, new ToolResult(0, string.Empty, false));

        await new DotnetBuildRunner(runner, TimeSpan.FromMinutes(1))
            .BuildAsync(Project, SarifDirectory, noIncremental: true, noDependencies: false, CancellationToken.None);

        Assert.Equal(["--no-incremental", "--no-dependencies"], Assert.Single(runner.Calls).TakeLast(2));
    }

    [Fact]
    public async Task No_dependencies_build_skips_only_the_referenced_projects()
    {
        var runner = new FakeProcessRunner().Respond(_ => true, new ToolResult(0, string.Empty, false));

        await new DotnetBuildRunner(runner, TimeSpan.FromMinutes(1))
            .BuildAsync(Project, SarifDirectory, noIncremental: false, noDependencies: true, CancellationToken.None);

        IReadOnlyList<string> arguments = Assert.Single(runner.Calls);
        Assert.Equal("--no-dependencies", arguments[^1]);
        Assert.DoesNotContain("--no-incremental", arguments);
    }

    [Fact]
    public async Task Forced_rebuild_without_dependencies_passes_the_switch_once()
    {
        var runner = new FakeProcessRunner().Respond(_ => true, new ToolResult(0, string.Empty, false));

        await new DotnetBuildRunner(runner, TimeSpan.FromMinutes(1))
            .BuildAsync(Project, SarifDirectory, noIncremental: true, noDependencies: true, CancellationToken.None);

        Assert.Single(Assert.Single(runner.Calls), a => a == "--no-dependencies");
    }

    [Fact]
    public async Task Build_properties_escape_semicolons_and_commas()
    {
        // MSBuild splits -p: values on ';' and ',' and unescapes %XX, so a repository path holding them must be
        // escaped; the project argument itself is a plain path.
        string root = Path.Combine(Path.GetTempPath(), "repo;with,odd%chars");
        string project = Path.Combine(root, "App", "App.csproj");
        string sarifDirectory = Path.Combine(root, ".git", "fdg", "a;b,c");
        var runner = new FakeProcessRunner().Respond(_ => true, new ToolResult(0, string.Empty, false));

        await new DotnetBuildRunner(runner, TimeSpan.FromMinutes(1))
            .BuildAsync(project, sarifDirectory, noIncremental: false, noDependencies: false, CancellationToken.None);

        string escapedRoot = Path.Combine(Path.GetTempPath(), "repo%3Bwith%2Codd%25chars");
        IReadOnlyList<string> arguments = Assert.Single(runner.Calls);
        Assert.Equal(project, arguments[1]);
        Assert.Contains("-p:FdgSarifDir=" + Path.Combine(escapedRoot, ".git", "fdg", "a%3Bb%2Cc"), arguments);
        Assert.Contains("-p:FdgSarifProject=" + Path.Combine(escapedRoot, "App", "App.csproj"), arguments);
    }
}
