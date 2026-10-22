using System.Diagnostics;
using System.Text.Json;
using Xunit.Abstractions;

namespace Fdg.IntegrationTests;

/// <summary>Runs the installed <c>dotnet fdg hook</c> entry points in a throwaway git repository.</summary>
[Trait("Category", "Integration")]
public sealed class HookEndToEndTests : IClassFixture<PackageFixture>
{
    private const string CleanSource =
        """
        namespace Sample
        {
            public class Worker
            {
                public void Run()
                {
                }
            }
        }

        """;

    // A private field without the "_" prefix (IDE1006: a rename, never fixed automatically) and a single-line
    // catch (FDGA003: expanded by the Stop hook's format step).
    private const string ChangedSource =
        """
        namespace Sample
        {
            public class Worker
            {
                private int count;

                public void Run()
                {
                    try
                    {
                        count++;
                    }
                    catch { }
                }
            }
        }

        """;

    // The compiler server would keep the package's analyzers locked after the test.
    private static readonly Dictionary<string, string> Environment = new() { ["UseSharedCompilation"] = "false" };

    private readonly PackageFixture _fixture;
    private readonly ITestOutputHelper _log;

    public HookEndToEndTests(PackageFixture fixture, ITestOutputHelper log)
    {
        _fixture = fixture;
        _log = log;
    }

    [Fact]
    public void Stop_formats_builds_and_blocks_on_unfixable_issue_in_changed_lines()
    {
        using var workspace = new SampleWorkspace(_fixture, "hook");
        string project = workspace.AddProject("Sample", "application", CleanSource);
        DotnetResult warmUp = workspace.Build(project);
        Assert.True(warmUp.ExitCode == 0, warmUp.Output);
        workspace.Write(".gitignore", "bin/\nobj/\n");
        Git(workspace, "init", "-q");
        Git(workspace, "add", "-A");
        Git(workspace, "-c", "user.name=FDG Test", "-c", "user.email=fdg@example.invalid", "commit", "-q", "-m", "init");
        string json = JsonSerializer.Serialize(new { session_id = "e2e", cwd = workspace.DirectoryPath, hook_event_name = "UserPromptSubmit" });

        DotnetResult turnStart = workspace.FdgHook("turn-start", json, Environment);
        Assert.True(turnStart.ExitCode == 0 && turnStart.Output.Trim().Length == 0, turnStart.Output);
        workspace.Write(Path.Combine("Sample", "Sample.cs"), ChangedSource);

        var stopwatch = Stopwatch.StartNew();
        DotnetResult stop = workspace.FdgHook("turn-stop", json.Replace("UserPromptSubmit", "Stop"), Environment);
        _log.WriteLine($"turn-stop ({stopwatch.Elapsed.TotalSeconds:F1} s, exit {stop.ExitCode}):\n{stop.Output}");

        Assert.Equal(0, stop.ExitCode);
        using JsonDocument output = JsonDocument.Parse(stop.Output);
        Assert.Equal("block", output.RootElement.GetProperty("decision").GetString());
        string reason = output.RootElement.GetProperty("reason").GetString()!;
        Assert.StartsWith("FDG found 1 issue(s) in lines you changed this turn.", reason);
        Assert.Contains("Sample/Sample.cs:5 IDE1006", reason);
        // Of the rules IDE1006 checks, the private-field prefix rule fits a private field best, so it comes first.
        Assert.Contains("    private int count;\n    FDGA055 DO use a prefix", reason);
        // The single-line catch was expanded by the format step, so it is not reported.
        Assert.DoesNotContain("FDGA003", reason);
        string formatted = workspace.Read(Path.Combine("Sample", "Sample.cs"));
        Assert.DoesNotContain("catch { }", formatted);

        // The AI fixes the field; the next Stop in the same turn is silent.
        workspace.Write(Path.Combine("Sample", "Sample.cs"), formatted.Replace("count", "_count"));
        stopwatch.Restart();
        DotnetResult secondStop = workspace.FdgHook("turn-stop", json.Replace("UserPromptSubmit", "Stop"), Environment);
        _log.WriteLine($"second turn-stop ({stopwatch.Elapsed.TotalSeconds:F1} s, exit {secondStop.ExitCode}):\n{secondStop.Output}");
        Assert.True(secondStop.ExitCode == 0 && secondStop.Output.Trim().Length == 0, secondStop.Output);

        // Nothing staged or committed.
        Assert.Equal(" M Sample/Sample.cs", Git(workspace, "status", "--porcelain").TrimEnd());
    }

    private static string Git(SampleWorkspace workspace, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workspace.DirectoryPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', arguments)}: {error}");
        return output;
    }
}
