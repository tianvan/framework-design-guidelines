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
            .BuildAsync(Project, SarifDirectory, noIncremental: false, CancellationToken.None);

        Assert.Equal(
            ["build", Project, "--no-restore", "-p:FdgSarifDir=" + SarifDirectory, "-p:FdgSarifProject=" + Project],
            Assert.Single(runner.Calls));
    }

    [Fact]
    public async Task Forced_rebuild_recompiles_only_the_project()
    {
        var runner = new FakeProcessRunner().Respond(_ => true, new ToolResult(0, string.Empty, false));

        await new DotnetBuildRunner(runner, TimeSpan.FromMinutes(1))
            .BuildAsync(Project, SarifDirectory, noIncremental: true, CancellationToken.None);

        Assert.Equal(["--no-incremental", "--no-dependencies"], Assert.Single(runner.Calls).TakeLast(2));
    }
}
