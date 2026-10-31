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
            [],
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
            .FormatAsync(Path.Combine(Path.GetTempPath(), "A.csproj"), null, [], ["FDGA003"], CancellationToken.None);

        Assert.DoesNotContain("--include", runner.Arguments);
    }

    [Fact]
    public async Task No_diagnostics_formats_whitespace_only()
    {
        string project = Path.Combine(Path.GetTempPath(), "A", "A.csproj");
        var runner = new RecordingRunner();

        await new DotnetFormatRunner(runner, TimeSpan.FromMinutes(1))
            .FormatAsync(project, ["A.cs"], [], [], CancellationToken.None);

        Assert.Equal(["format", "whitespace", project, "--no-restore", "--include", "A.cs"], runner.Arguments);
    }

    [Fact]
    public async Task Long_include_list_does_not_exceed_the_command_line_limit()
    {
        // dotnet format is forwarded: the dotnet CLI expands a response file itself and starts
        // "dotnet exec <path>/dotnet-format.dll" with every argument, so only batches keep each line short enough.
        string project = Path.Combine(Path.GetTempPath(), "中文 目录", "A", "A.csproj");
        string[] includes = Enumerable.Range(0, 2000)
            .Select(i => $"src/a rather long directory name/中文 目录/{i:D4}/SomeFairlyLongTypeNameForTheTest{i}.cs")
            .Append("@starts with an at sign.cs")
            .ToArray();
        var runner = new RecordingRunner();

        await new DotnetFormatRunner(runner, TimeSpan.FromMinutes(1))
            .FormatAsync(project, includes, ["Broken.cs"], ["FDGA003"], CancellationToken.None);

        Assert.True(runner.Calls.Count > 1, $"{runner.Calls.Count} call(s)");
        Assert.All(runner.Calls, call =>
        {
            // Each argument costs at most its length, a separating space and two quotes; 1000 characters are left
            // for the forwarder's "exec <path to dotnet-format.dll>".
            Assert.InRange("dotnet".Length + call.Sum(a => a.Length + 3), 0, 30000 - 1000);
            Assert.Equal(["format", project, "--no-restore", "--severity", "warn", "--include"], call.Take(6));
            Assert.Equal(["--exclude", "Broken.cs", "--diagnostics", "FDGA003"], call.TakeLast(4));
        });
        // Every file is included exactly once, in order; a name starting with "@" is not read as a response file.
        Assert.Equal(
            includes.Select(i => i.StartsWith('@') ? "./" + i : i),
            runner.Calls.SelectMany(call => call.Skip(6).SkipLast(4)));
    }

    [Fact]
    public async Task Excludes_follow_the_includes()
    {
        string project = Path.Combine(Path.GetTempPath(), "A", "A.csproj");
        var runner = new RecordingRunner();

        await new DotnetFormatRunner(runner, TimeSpan.FromMinutes(1))
            .FormatAsync(project, null, ["中文 目录/Broken.cs", "@odd.cs"], [], CancellationToken.None);

        Assert.Equal(
            ["format", "whitespace", project, "--no-restore", "--exclude", "中文 目录/Broken.cs", "./@odd.cs"],
            runner.Arguments);
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

        public List<IReadOnlyList<string>> Calls { get; } = [];

        public Task<ToolResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            FileName = fileName;
            Arguments = arguments.ToArray();
            WorkingDirectory = workingDirectory;
            Calls.Add(Arguments);
            return Task.FromResult(new ToolResult(0, string.Empty, false));
        }
    }
}
