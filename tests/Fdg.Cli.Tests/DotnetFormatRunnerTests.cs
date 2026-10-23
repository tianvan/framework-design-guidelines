using Fdg.Cli.Tooling;

namespace Fdg.Cli.Tests;

public sealed class DotnetFormatRunnerTests
{
    [Fact]
    public async Task Runs_dotnet_format_in_the_project_directory_with_includes_and_diagnostics()
    {
        string project = Path.Combine(Path.GetTempPath(), "中文 目录", "A", "A.csproj");
        var runner = new RecordingRunner();

        await new DotnetFormatRunner(runner, TimeSpan.FromMinutes(1)).FormatAsync(
            project,
            ["src/中文 目录/A.cs", "src/深 层/"],
            ["FDGA003", "IDE0055"],
            CancellationToken.None);

        Assert.Equal("dotnet", runner.FileName);
        Assert.Equal(Path.GetDirectoryName(project), runner.WorkingDirectory);
        Assert.Equal(
            [
                "format", project, "--no-restore", "--severity", "warn",
                "--include", "src/中文 目录/A.cs", "src/深 层/",
                "--diagnostics", "FDGA003", "IDE0055",
            ],
            runner.Arguments);
    }

    [Fact]
    public async Task Whole_project_has_no_include()
    {
        var runner = new RecordingRunner();

        await new DotnetFormatRunner(runner, TimeSpan.FromMinutes(1))
            .FormatAsync(Path.Combine(Path.GetTempPath(), "A.csproj"), null, ["FDGA003"], CancellationToken.None);

        Assert.DoesNotContain("--include", runner.Arguments);
    }

    [Fact]
    public async Task No_diagnostics_formats_whitespace_only()
    {
        string project = Path.Combine(Path.GetTempPath(), "A", "A.csproj");
        var runner = new RecordingRunner();

        await new DotnetFormatRunner(runner, TimeSpan.FromMinutes(1))
            .FormatAsync(project, ["A.cs"], [], CancellationToken.None);

        Assert.Equal(["format", "whitespace", project, "--no-restore", "--include", "A.cs"], runner.Arguments);
    }

    [Fact]
    public void Includes_are_relative_to_the_project_with_forward_slashes()
    {
        using var tree = new TempTree();
        string project = tree.Write("A/A.csproj", TempTree.SdkProject);
        string file = tree.Write("A/中文 目录/X.cs");
        string directory = tree.Directory("A/深 层");

        Assert.Equal(
            ["中文 目录/X.cs", "深 层/"],
            FormatIncludes.ToProjectRelative(project, [file, directory]));
    }

    private sealed class RecordingRunner : IProcessRunner
    {
        public string? FileName { get; private set; }

        public string? WorkingDirectory { get; private set; }

        public IReadOnlyList<string> Arguments { get; private set; } = [];

        public Task<ToolResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            FileName = fileName;
            Arguments = arguments;
            WorkingDirectory = workingDirectory;
            return Task.FromResult(new ToolResult(0, string.Empty, false));
        }
    }
}
