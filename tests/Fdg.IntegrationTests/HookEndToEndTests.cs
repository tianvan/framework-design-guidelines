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
    // catch (FDGA003: expanded by the Stop hook's format step). The catch names a specific exception, since a catch of
    // every exception that swallows it is CA1031.
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
                    catch (System.InvalidOperationException) { }
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
        InitRepository(workspace);
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
        Assert.DoesNotContain("InvalidOperationException) { }", formatted);

        // The AI fixes the field; the next Stop in the same turn is silent.
        workspace.Write(Path.Combine("Sample", "Sample.cs"), formatted.Replace("count", "_count"));
        stopwatch.Restart();
        DotnetResult secondStop = workspace.FdgHook("turn-stop", json.Replace("UserPromptSubmit", "Stop"), Environment);
        _log.WriteLine($"second turn-stop ({stopwatch.Elapsed.TotalSeconds:F1} s, exit {secondStop.ExitCode}):\n{secondStop.Output}");
        Assert.True(secondStop.ExitCode == 0 && secondStop.Output.Trim().Length == 0, secondStop.Output);

        // Nothing staged or committed.
        Assert.Equal(" M Sample/Sample.cs", Git(workspace, "status", "--porcelain").TrimEnd());
    }

    [Fact]
    public void Stop_applies_no_fix_of_diagnostics_FDG_does_not_register()
    {
        using var workspace = new SampleWorkspace(_fixture, "hook strict", FixEndToEndTests.StrictEditorConfig);
        string project = workspace.AddProject("Strict", "application", CleanSource, FixEndToEndTests.StrictProperties);
        DotnetResult warmUp = workspace.Build(project);
        Assert.True(warmUp.ExitCode == 0, warmUp.Output);
        InitRepository(workspace);
        string json = JsonSerializer.Serialize(new { session_id = "e2e", cwd = workspace.DirectoryPath, hook_event_name = "UserPromptSubmit" });
        DotnetResult turnStart = workspace.FdgHook("turn-start", json, Environment);
        Assert.True(turnStart.ExitCode == 0 && turnStart.Output.Trim().Length == 0, turnStart.Output);

        workspace.Write(Path.Combine("Strict", "Strict.cs"), FixEndToEndTests.StrictSource);
        DotnetResult stop = workspace.FdgHook("turn-stop", json.Replace("UserPromptSubmit", "Stop"), Environment);
        _log.WriteLine($"turn-stop (exit {stop.ExitCode}):\n{stop.Output}");

        Assert.Equal(0, stop.ExitCode);
        FixEndToEndTests.AssertOnlyFdgFixes(workspace.Read(Path.Combine("Strict", "Strict.cs")));
    }

    [Fact]
    public void Up_to_date_project_whose_reference_recompiles_is_still_checked()
    {
        // Review experiment 2: App references Lib. With reference assemblies, a change to a Lib method body leaves
        // App's CoreCompile skipped while Lib recompiles; only App's own log may count for App.
        using var workspace = new SampleWorkspace(_fixture, "reference");
        const string ReferenceAssembly = "<ProduceReferenceAssembly>true</ProduceReferenceAssembly>";
        workspace.AddProject("Lib", "application", LibSource(1), ReferenceAssembly);
        string app = workspace.AddProjectFromXml(
            "App",
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>netstandard2.0</TargetFramework>
                <LangVersion>7.3</LangVersion>
                <FrameworkDesignGuidelinesProfile>application</FrameworkDesignGuidelinesProfile>
                {ReferenceAssembly}
              </PropertyGroup>
              <ItemGroup>
                {workspace.PackageReference}
                <ProjectReference Include="../Lib/Lib.csproj" />
              </ItemGroup>
            </Project>
            """,
            CleanSource);
        DotnetResult warmUp = workspace.Build(app);
        Assert.True(warmUp.ExitCode == 0, warmUp.Output);
        InitRepository(workspace);
        string json = JsonSerializer.Serialize(new { session_id = "e2e", cwd = workspace.DirectoryPath, hook_event_name = "UserPromptSubmit" });
        DotnetResult turnStart = workspace.FdgHook("turn-start", json, Environment);
        Assert.True(turnStart.ExitCode == 0 && turnStart.Output.Trim().Length == 0, turnStart.Output);

        // The AI changes App, builds it itself, then changes only a method body in Lib.
        workspace.Write(Path.Combine("App", "App.cs"), ChangedSource.Replace(
            "InvalidOperationException) { }",
            "InvalidOperationException)\n            {\n            }"));
        DotnetResult aiBuild = workspace.Build(app);
        Assert.True(aiBuild.ExitCode == 0, aiBuild.Output);
        workspace.Write(Path.Combine("Lib", "Lib.cs"), LibSource(2));
        DotnetResult stop = workspace.FdgHook("turn-stop", json.Replace("UserPromptSubmit", "Stop"), Environment);
        _log.WriteLine($"turn-stop (exit {stop.ExitCode}):\n{stop.Output}");

        Assert.Equal(0, stop.ExitCode);
        using JsonDocument output = JsonDocument.Parse(stop.Output);
        Assert.Equal("block", output.RootElement.GetProperty("decision").GetString());
        Assert.Contains("App/App.cs:5 IDE1006", output.RootElement.GetProperty("reason").GetString()!);
    }

    private static string LibSource(int value) =>
        $$"""
        namespace Lib
        {
            public class Helper
            {
                public int Get()
                {
                    return {{value}};
                }
            }
        }

        """;

    private static void InitRepository(SampleWorkspace workspace)
    {
        workspace.Write(".gitignore", "bin/\nobj/\n");
        Git(workspace, "init", "-q");
        Git(workspace, "add", "-A");
        Git(workspace, "-c", "user.name=FDG Test", "-c", "user.email=fdg@example.invalid", "commit", "-q", "-m", "init");
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
