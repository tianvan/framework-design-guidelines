using System.Text.Json;
using Xunit.Abstractions;

namespace Fdg.IntegrationTests;

/// <summary>
/// Runs the plugin's hook wrappers in a repository whose local tool manifest pins this run's dotnet-fdg, so that they
/// go through <c>dotnet tool run</c> with the real SDK.
/// </summary>
[Trait("Category", "Integration")]
public sealed class LocalToolEndToEndTests : IClassFixture<PackageFixture>
{
    private const string Header = "This repository follows Framework Design Guidelines (3rd ed.).";

    private const string Source = "namespace Sample { }\n";

    private readonly PackageFixture _fixture;
    private readonly ITestOutputHelper _log;

    public LocalToolEndToEndTests(PackageFixture fixture, ITestOutputHelper log)
    {
        _fixture = fixture;
        _log = log;
    }

    [BashFact]
    public void Bash_wrapper_runs_the_tool_pinned_in_the_local_manifest()
    {
        using var workspace = CreateRepository("local tool sh");

        DotnetResult result = RunWrapper(workspace, Shells.Bash!, [WrapperPath("fdg-hook.sh"), "session-start"]);

        AssertSessionStart(result);
    }

    [PwshFact]
    public void Pwsh_wrapper_runs_the_tool_pinned_in_the_local_manifest()
    {
        using var workspace = CreateRepository("local tool ps");

        DotnetResult result = RunWrapper(
            workspace,
            Shells.Pwsh!,
            ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", WrapperPath("fdg-hook.ps1"), "session-start"]);

        AssertSessionStart(result);
    }

    /// <summary>
    /// A workspace with an onboarded project and a <c>.config/dotnet-tools.json</c> that pins this run's dotnet-fdg,
    /// restored with <c>dotnet tool restore</c> as a clone of such a repository would be.
    /// </summary>
    private SampleWorkspace CreateRepository(string name)
    {
        // Packs the tool into its feed.
        _ = _fixture.FdgToolPath;
        var workspace = new SampleWorkspace(_fixture, name);
        try
        {
            workspace.AddProject("Sample", "application", Source);
            workspace.Write(
                Path.Combine(".config", "dotnet-tools.json"),
                $$"""
                {
                  "version": 1,
                  "isRoot": true,
                  "tools": {
                    "dotnet-fdg": {
                      "version": "{{_fixture.Version}}",
                      "commands": [
                        "dotnet-fdg"
                      ],
                      "rollForward": false
                    }
                  }
                }

                """);
            DotnetResult restore = workspace.Dotnet("tool", "restore", "--add-source", _fixture.ToolFeedDirectory);
            Assert.True(restore.ExitCode == 0, "dotnet tool restore failed:\n" + restore.Output);
            return workspace;
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Runs the shell in the workspace with a SessionStart payload on stdin. NUGET_PACKAGES points at this run's
    /// packages folder, where <c>dotnet tool restore</c> put the tool.
    /// </summary>
    private DotnetResult RunWrapper(SampleWorkspace workspace, string shell, IReadOnlyList<string> arguments)
    {
        string json = JsonSerializer.Serialize(
            new { session_id = "local-tool", cwd = workspace.DirectoryPath, hook_event_name = "SessionStart" });
        DotnetResult result = Dotnet.RunProgram(
            shell,
            workspace.DirectoryPath,
            _fixture.PackagesDirectory,
            json,
            new Dictionary<string, string>(),
            arguments);
        _log.WriteLine($"{Path.GetFileName(shell)} (exit {result.ExitCode}):\n{result.Output}");
        return result;
    }

    private string WrapperPath(string fileName) =>
        Path.Combine(_fixture.RepositoryRoot, "plugins", "framework-design-guidelines", "hooks", fileName);

    /// <summary>
    /// The tool itself answered: a wrapper that could not run it would print a <c>systemMessage</c> with no context.
    /// </summary>
    private static void AssertSessionStart(DotnetResult result)
    {
        Assert.True(result.ExitCode == 0, $"The wrapper exited with {result.ExitCode}:\n{result.Output}");
        using JsonDocument output = JsonDocument.Parse(result.Output);
        JsonElement hook = output.RootElement.GetProperty("hookSpecificOutput");
        Assert.Equal("SessionStart", hook.GetProperty("hookEventName").GetString());
        Assert.StartsWith(Header + "\n", hook.GetProperty("additionalContext").GetString());
    }
}

/// <summary>Locates the shells that run the hook wrappers on this machine.</summary>
internal static class Shells
{
    public static string? Bash { get; } = FindBash();

    public static string? Pwsh { get; } = FindOnPath(OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh");

    private static string? FindBash()
    {
        if (!OperatingSystem.IsWindows())
        {
            return FindOnPath("bash");
        }

        // Not System32\bash.exe, which starts WSL: the Git Bash that Claude Code uses, found from git.exe.
        string? git = FindOnPath("git.exe");
        for (DirectoryInfo? directory = git is null ? null : new FileInfo(git).Directory;
            directory is not null;
            directory = directory.Parent)
        {
            string bash = Path.Combine(directory.FullName, "usr", "bin", "bash.exe");
            if (File.Exists(bash))
            {
                return bash;
            }
        }

        return null;
    }

    private static string? FindOnPath(string fileName)
    {
        return (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory, fileName))
            .FirstOrDefault(File.Exists);
    }
}

public sealed class BashFactAttribute : FactAttribute
{
    public BashFactAttribute()
    {
        if (Shells.Bash is null)
        {
            Skip = "bash is not available.";
        }
    }
}

public sealed class PwshFactAttribute : FactAttribute
{
    public PwshFactAttribute()
    {
        if (Shells.Pwsh is null)
        {
            Skip = "pwsh is not available.";
        }
    }
}
