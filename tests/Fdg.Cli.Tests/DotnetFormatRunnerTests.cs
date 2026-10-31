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
    public async Task Long_include_list_does_not_exceed_the_command_line_limit()
    {
        string project = Path.Combine(Path.GetTempPath(), "中文 目录", "A", "A.csproj");
        string[] includes = Enumerable.Range(0, 2000)
            .Select(i => $"src/a rather long directory name/中文 目录/{i:D4}/SomeFairlyLongTypeNameForTheTest{i}.cs")
            .Append("@starts with an at sign.cs")
            .Append("#starts with a hash.cs")
            .ToArray();
        var runner = new RecordingRunner();

        await new DotnetFormatRunner(runner, TimeSpan.FromMinutes(1))
            .FormatAsync(project, includes, ["FDGA003"], CancellationToken.None);

        Assert.NotEmpty(runner.Calls);
        // Each argument costs at most its length, a separating space and two quotes.
        Assert.All(runner.Calls, call => Assert.InRange("dotnet".Length + call.Arguments.Sum(a => a.Length + 3), 0, 30000));
        // Every file is still included, each exactly once, whichever way the list was passed.
        Assert.Equal(
            includes.Select(i => i.StartsWith('@') || i.StartsWith('#') ? "./" + i : i),
            runner.Calls.SelectMany(call => call.Includes));
        Assert.All(runner.Calls, call => Assert.Equal(["--diagnostics", "FDGA003"], call.Arguments.TakeLast(2)));
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

    private sealed record RecordedCall(IReadOnlyList<string> Arguments, IReadOnlyList<string> Includes);

    private sealed class RecordingRunner : IProcessRunner
    {
        public string? FileName { get; private set; }

        public string? WorkingDirectory { get; private set; }

        public IReadOnlyList<string> Arguments { get; private set; } = [];

        public List<RecordedCall> Calls { get; } = [];

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
            Calls.Add(new RecordedCall(Arguments, Includes(arguments)));
            return Task.FromResult(new ToolResult(0, string.Empty, false));
        }

        /// <summary>
        /// The values after <c>--include</c>, with a response file read as dotnet format reads it: one value per
        /// line. It is read now, since the runner deletes it after the call.
        /// </summary>
        private static List<string> Includes(IReadOnlyList<string> arguments)
        {
            var includes = new List<string>();
            int start = arguments.ToList().IndexOf("--include");
            if (start < 0)
            {
                return includes;
            }

            foreach (string argument in arguments.Skip(start + 1).TakeWhile(a => !a.StartsWith("--", StringComparison.Ordinal)))
            {
                if (argument.StartsWith('@'))
                {
                    includes.AddRange(File.ReadAllLines(argument[1..]).Where(line => line.Length > 0));
                }
                else
                {
                    includes.Add(argument);
                }
            }

            return includes;
        }
    }
}
