using System.Security;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit.Abstractions;

namespace Fdg.IntegrationTests;

[Trait("Category", "Integration")]
public sealed class AgentIntegrationTests(PackageFixture fixture, ITestOutputHelper log) : IClassFixture<PackageFixture>
{
    private const string Source = "namespace Sample { }\n";

    private const string Existing = """
        {
          "env": { "EXISTING": "yes" },
          "hooks": {
            "Stop": [{ "matcher": "", "hooks": [{ "type": "command", "command": "echo existing" }] }]
          }
        }
        """;

    [BashFact]
    public void Packed_tool_installs_project_hooks_and_reuses_an_offline_cache()
    {
        using SampleWorkspace workspace = CreateRepository("agent bash $cache");
        var env = CacheEnvironment("shared");
        const string launcher = ".codex/hooks/fdg/fdg.sh";

        Check(Run(workspace, "dotnet", env, ["tool", "exec", "--yes", "dotnet-fdg@" + fixture.Version, "--", "init", "--host", "codex", "--dry-run"]));
        Assert.False(Directory.Exists(Path.Combine(workspace.DirectoryPath, ".codex")));
        Assert.False(Directory.Exists(env["FDG_TOOL_CACHE"]));

        Check(Run(workspace, "dotnet", env, ["tool", "exec", "--yes", "dotnet-fdg@" + fixture.Version, "--", "init", "--host", "codex", "--yes"]));
        AssertLayout(workspace);

        const string project = "src/src.csproj";
        workspace.Write("Directory.Build.props", "<Project><PropertyGroup><UseArtifactsOutput>true</UseArtifactsOutput></PropertyGroup></Project>");
        Check(workspace.Build(Path.Combine(workspace.DirectoryPath, project)));
        using (JsonDocument evaluated = JsonDocument.Parse(Check(Run(workspace, Shells.Bash!, env, [launcher, "project", project]))))
        {
            Assert.True(evaluated.RootElement.GetProperty("markerFound").GetBoolean());
            Assert.Equal("application", evaluated.RootElement.GetProperty("profile").GetString());
        }

        workspace.Write("nuget.config", "<configuration><packageSources><clear /></packageSources></configuration>");
        Assert.Equal(fixture.Version, Check(Run(workspace, Shells.Bash!, env, [launcher, "--version"])).Trim());
        string input = Payload(workspace, "SessionStart");
        DotnetResult session = RunDeclaredBash(workspace, env, "SessionStart", input);
        AssertSessionStart(session);

        var empty = CacheEnvironment("cold");
        string hint = Check(RunDeclaredBash(workspace, empty, "SessionStart", input));
        Assert.Contains(launcher + " prepare", hint);
        Assert.Empty(Check(RunDeclaredBash(workspace, empty, "UserPromptSubmit", Payload(workspace, "UserPromptSubmit"))));

        WriteFeedConfig(workspace);
        Check(Run(workspace, Shells.Bash!, empty, [launcher, "prepare"]));
        AssertSessionStart(RunDeclaredBash(workspace, empty, "SessionStart", input));
        Assert.False(Directory.Exists(Path.Combine(workspace.DirectoryPath, ".config")));
    }

    [PwshFact]
    public void PowerShell_command_reads_hook_stdin_and_runs_from_a_subdirectory()
    {
        using SampleWorkspace workspace = CreateRepository("agent pwsh $cache 'quote");
        var env = CacheEnvironment("pwsh");
        Check(Run(workspace, fixture.FdgToolPath, env, ["init", "--host", "codex", "--yes"]));
        string command = ReadCodexCommand(workspace, "SessionStart", "commandWindows");
        string cwd = Path.Combine(workspace.DirectoryPath, "src");
        DotnetResult result = Dotnet.RunProgram(Shells.Pwsh!, cwd, fixture.PackagesDirectory,
            Payload(workspace, "SessionStart", cwd), env, ["-NoProfile", "-Command", command]);
        AssertSessionStart(result);
    }

    [Fact]
    public void Adding_a_host_and_removing_the_reference_owner_preserves_team_settings()
    {
        using SampleWorkspace workspace = CreateRepository("agent lifecycle");
        var env = CacheEnvironment("lifecycle");
        workspace.Write(".claude/settings.json", Existing);
        workspace.Write(".codex/hooks.json", Existing);
        workspace.Write(".config/dotnet-tools.json", "{\"tools\":{}}\n");

        Check(Run(workspace, fixture.FdgToolPath, env, ["init", "--host", "claude", "--yes"]));
        Check(Run(workspace, fixture.FdgToolPath, env, ["init", "--host", "codex", "--yes"]));
        Assert.Single(ReferenceDirectories(workspace), path => path.Contains(".claude"));
        AssertLinks(workspace, ".agents/skills");
        AssertLinks(workspace, ".claude/skills");
        Assert.Equal("No changes.", Check(Run(workspace, fixture.FdgToolPath, env, ["init", "--host", "both", "--yes"])).Trim());
        Assert.Equal("{\"tools\":{}}\n", workspace.Read(".config/dotnet-tools.json"));

        workspace.Write("src/src.csproj", "");
        Check(Run(workspace, fixture.FdgToolPath, env, ["uninstall", "--host", "claude", "--yes"]));
        Assert.Single(ReferenceDirectories(workspace), path => path.Contains(".agents"));
        AssertLinks(workspace, ".agents/skills");
        AssertExistingSettings(workspace.Read(".claude/settings.json"));

        const string skill = ".agents/skills/fdg-fix/SKILL.md";
        workspace.Write(skill, workspace.Read(skill) + "\nTeam notes.\n");
        Check(Run(workspace, fixture.FdgToolPath, env, ["uninstall", "--host", "codex", "--yes"]));
        Assert.Contains("Team notes.", workspace.Read(skill));
        AssertExistingSettings(workspace.Read(".codex/hooks.json"));
        Assert.Empty(ReferenceDirectories(workspace));
        Assert.True(Directory.Exists(env["FDG_TOOL_CACHE"]));
    }

    [Fact]
    public void Conflict_preflight_does_not_write_the_other_host()
    {
        using SampleWorkspace workspace = CreateRepository("agent conflict");
        var env = CacheEnvironment("conflict");
        const string toml = ".codex/config.toml";
        workspace.Write(toml, """
            [[hooks.Stop]]
            hooks = [{ type = "command", command = "dotnet tool run dotnet-fdg -- hook turn-stop" }]
            """);
        DotnetResult conflict = Run(workspace, fixture.FdgToolPath, env, ["init", "--host", "both", "--yes"]);
        Assert.NotEqual(0, conflict.ExitCode);
        Assert.Contains(toml, conflict.Output);
        Assert.False(Directory.Exists(Path.Combine(workspace.DirectoryPath, ".claude")));
        Assert.False(Directory.Exists(env["FDG_TOOL_CACHE"]));

        workspace.Write(toml, "# unrelated setting\nmodel = 'gpt-6.2-sol'\n");
        workspace.Write(".claude/settings.json", "{\"disableAllHooks\":true}\n");
        DotnetResult disabled = Run(workspace, fixture.FdgToolPath, env, ["init", "--host", "both", "--yes"]);
        Assert.NotEqual(0, disabled.ExitCode);
        Assert.Contains("disableAllHooks", disabled.Output);
        Assert.False(File.Exists(Path.Combine(workspace.DirectoryPath, ".codex/hooks.json")));
        workspace.Write(".claude/settings.json", "{}\n");
        Check(Run(workspace, fixture.FdgToolPath, env, ["init", "--host", "both", "--yes"]));
        const string skill = ".agents/skills/fdg-fix/SKILL.md";
        workspace.Write(".claude/skills/fdg-write/SKILL.md", "Local rewrite.\n");
        string before = workspace.Read(skill);
        DotnetResult update = Run(workspace, fixture.FdgToolPath, env, ["init", "--host", "codex", "--yes"]);
        Assert.NotEqual(0, update.ExitCode);
        Assert.Contains(".claude/skills/fdg-write/SKILL.md", update.Output);
        Assert.Equal(before, workspace.Read(skill));
    }

    [BashFact]
    public void Worktree_fix_refuses_a_source_symlink()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // Creating a file symlink on Windows may require Developer Mode.
        }

        using SampleWorkspace workspace = CreateRepository("agent symlink");
        var env = CacheEnvironment("symlink");
        string external = Path.Combine(fixture.Root, "External.cs");
        File.WriteAllText(external, "public class External{}\n");
        File.CreateSymbolicLink(Path.Combine(workspace.DirectoryPath, "src", "External.cs"), external);
        Check(Run(workspace, fixture.FdgToolPath, env, ["init", "--host", "codex", "--yes"]));
        DotnetResult result = Run(workspace, Shells.Bash!, env, [".codex/hooks/fdg/fdg.sh", "fix", "src/External.cs"]);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("symbolic links", result.Output);
        Assert.Equal("public class External{}\n", File.ReadAllText(external));
    }

    [BashFact]
    public void Two_versions_coexist_and_git_rollback_selects_the_previous_tool()
    {
        using SampleWorkspace first = CreateRepository("agent rollback");
        using SampleWorkspace second = CreateRepository("agent second");
        var env = CacheEnvironment("side-by-side");
        const string script = ".codex/hooks/fdg/fdg.sh";
        Check(Run(first, fixture.FdgToolPath, env, ["init", "--host", "both", "--yes"]));
        Git(first, "add", ".agents", ".codex", ".claude");
        Git(first, "commit", "-qm", "Install FDG");
        Check(Run(second, fixture.FdgToolPath, env, ["init", "--host", "codex", "--yes"]));

        string next = fixture.Version + ".1";
        fixture.PackTool(next);
        Check(Run(first, "dotnet", env, ["tool", "exec", "--yes", "dotnet-fdg@" + next, "--", "init", "--host", "codex", "--yes"]));
        Assert.Equal(next, Check(Run(first, Shells.Bash!, env, [script, "--version"])).Trim());
        Assert.Contains(next, first.Read(".claude/hooks/fdg-install.json"));
        Assert.Equal(fixture.Version, Check(Run(second, Shells.Bash!, env, [script, "--version"])).Trim());
        Git(first, "restore", "--", ".agents", ".codex", ".claude");
        string tools = env["FDG_TOOL_CACHE"];
        string newerTool = Directory.GetFiles(tools, OperatingSystem.IsWindows() ? "dotnet-fdg.exe" : "dotnet-fdg", SearchOption.AllDirectories)
            .Single(path => Path.GetFileName(Path.GetDirectoryName(path)) == next);
        Check(Run(first, newerTool, env, ["uninstall", "--host", "codex", "--yes"]));
        Assert.Contains(fixture.Version, first.Read(".claude/hooks/fdg-install.json"));
        Assert.Single(ReferenceDirectories(first), path => path.Contains(".claude"));
        Git(first, "restore", "--", ".agents", ".codex", ".claude");
        first.Write("nuget.config", "<configuration><packageSources><clear /></packageSources></configuration>");
        Assert.Equal(fixture.Version, Check(Run(first, Shells.Bash!, env, [script, "--version"])).Trim());
        AssertSessionStart(RunDeclaredBash(first, env, "SessionStart", Payload(first, "SessionStart")));
    }

    [Fact]
    public void Concurrent_installations_can_share_a_cold_cache()
    {
        using SampleWorkspace first = CreateRepository("agent concurrent one");
        using SampleWorkspace second = CreateRepository("agent concurrent two");
        var env = CacheEnvironment("cold-shared");
        string[] init = ["init", "--host", "codex", "--yes"];
        Parallel.Invoke(
            () => Check(Run(first, fixture.FdgToolPath, env, init)),
            () => Check(Run(second, fixture.FdgToolPath, env, init)));
        Assert.Single(Directory.GetFiles(env["FDG_TOOL_CACHE"], OperatingSystem.IsWindows() ? "dotnet-fdg.exe" : "dotnet-fdg", SearchOption.AllDirectories));
    }

    [Fact]
    public void Retrying_an_interrupted_install_rolls_back_its_partial_files_first()
    {
        using SampleWorkspace workspace = CreateRepository("agent interrupted");
        var env = CacheEnvironment("interrupted");
        var id = Guid.NewGuid();
        const string path = ".codex/hooks.json";
        string temporary = $".codex/.fdg-{id:N}-hooks.json.tmp";
        byte[] content = "{}\n"u8.ToArray();
        workspace.Write(path, "{}\n");
        workspace.Write(temporary, "half written");
        workspace.Write(".git/fdg-install/transaction.json", JsonSerializer.Serialize(new
        {
            Id = id,
            Files = new[] { new { Path = path, Before = (byte[]?)null, After = content } },
            NewDirectories = new[] { ".codex" },
        }));

        DotnetResult preview = Run(workspace, fixture.FdgToolPath, env, ["init", "--host", "codex", "--dry-run"]);
        Assert.Contains("interrupted", preview.Output);
        Assert.Equal("{}\n", workspace.Read(path));
        Check(Run(workspace, fixture.FdgToolPath, env, ["init", "--host", "codex", "--yes"]));
        Assert.False(File.Exists(Path.Combine(workspace.DirectoryPath, temporary)));
        Assert.False(File.Exists(Path.Combine(workspace.DirectoryPath, ".git/fdg-install/transaction.json")));
        AssertLayout(workspace);
    }

    [BashFact]
    public void A_declaration_from_the_primary_checkout_cannot_activate_an_uninstalled_worktree()
    {
        using SampleWorkspace primary = CreateRepository("agent worktree");
        using var linked = new SampleWorkspace(fixture, "linked parent");
        var env = CacheEnvironment("worktree");
        string linkedPath = Path.Combine(linked.DirectoryPath, "without FDG");
        Git(primary, "add", "src/src.csproj", "src/src.cs");
        Git(primary, "commit", "-qm", "Initial project");
        Git(primary, "worktree", "add", "--detach", linkedPath);
        Check(Run(primary, fixture.FdgToolPath, env, ["init", "--host", "codex", "--yes"]));
        string command = ReadCodexCommand(primary, "SessionStart", "command");
        DotnetResult inactive = Dotnet.RunProgram(Shells.Bash!, linkedPath, fixture.PackagesDirectory,
            Payload(primary, "SessionStart", linkedPath), env, ["-c", command]);
        Assert.Empty(Check(inactive));

        string outsidePayload = Payload(primary, "UserPromptSubmit", linkedPath);
        Assert.Empty(Check(RunDeclaredBash(primary, env, "UserPromptSubmit", outsidePayload)));
        Assert.False(Directory.Exists(Path.Combine(primary.DirectoryPath, ".git/fdg/agent-test")));
    }

    private SampleWorkspace CreateRepository(string name)
    {
        _ = fixture.FdgToolPath;
        var workspace = new SampleWorkspace(fixture, name);
        try
        {
            Check(Run(workspace, "git", new Dictionary<string, string>(), ["init", "-q"]));
            WriteFeedConfig(workspace);
            string project = workspace.AddProjectFromXml("src", $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <FrameworkDesignGuidelinesProfile>application</FrameworkDesignGuidelinesProfile>
                  </PropertyGroup>
                  <ItemGroup>
                    {workspace.PackageReference}
                  </ItemGroup>
                </Project>
                """, Source);
            Check(workspace.Build(project));
            return workspace;
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    private void WriteFeedConfig(SampleWorkspace workspace) => workspace.Write("nuget.config", $"""
        <configuration>
          <packageSources>
            <clear />
            <add key="analyzers" value="{SecurityElement.Escape(fixture.FeedDirectory)}" />
            <add key="tool" value="{SecurityElement.Escape(fixture.ToolFeedDirectory)}" />
          </packageSources>
        </configuration>
        """);

    private Dictionary<string, string> CacheEnvironment(string name) => new()
    {
        ["FDG_TOOL_CACHE"] = Path.Combine(fixture.Root, name + "-" + Guid.NewGuid().ToString("N"), "tools"),
        ["DOTNET_CLI_HOME"] = Path.Combine(fixture.Root, "cli"),
    };

    private DotnetResult Run(SampleWorkspace workspace, string executable, IReadOnlyDictionary<string, string> env,
        IReadOnlyList<string> args, string? input = null) =>
        Dotnet.RunProgram(executable, workspace.DirectoryPath, fixture.PackagesDirectory, input, env, args);

    private void Git(SampleWorkspace workspace, params string[] args) =>
        Check(Run(workspace, "git", new Dictionary<string, string>(),
            ["-c", "user.name=FDG Test", "-c", "user.email=fdg@example.invalid", "-c", "commit.gpgsign=false", .. args]));

    private DotnetResult RunDeclaredBash(SampleWorkspace workspace, IReadOnlyDictionary<string, string> env, string name, string input) =>
        Run(workspace, Shells.Bash!, env, ["-c", ReadCodexCommand(workspace, name, "command")], input);

    private static string ReadCodexCommand(SampleWorkspace workspace, string name, string property)
    {
        using JsonDocument settings = JsonDocument.Parse(workspace.Read(".codex/hooks.json"));
        return settings.RootElement.GetProperty("hooks").GetProperty(name)[0].GetProperty("hooks")[0].GetProperty(property).GetString()!;
    }

    private string Check(DotnetResult result)
    {
        log.WriteLine(result.Output);
        Assert.True(result.ExitCode == 0, $"Exit code {result.ExitCode}:\n{result.Output}");
        return result.Output;
    }

    private static string Payload(SampleWorkspace workspace, string name, string? cwd = null) =>
        JsonSerializer.Serialize(new { session_id = "agent-test", cwd = cwd ?? workspace.DirectoryPath, hook_event_name = name });

    private void AssertSessionStart(DotnetResult result)
    {
        using JsonDocument output = JsonDocument.Parse(Check(result));
        JsonElement hook = output.RootElement.GetProperty("hookSpecificOutput");
        Assert.Equal("SessionStart", hook.GetProperty("hookEventName").GetString());
        Assert.Contains("This repository follows Framework Design Guidelines", hook.GetProperty("additionalContext").GetString());
    }

    private static string[] ReferenceDirectories(SampleWorkspace workspace) =>
        Directory.GetDirectories(workspace.DirectoryPath, "references", SearchOption.AllDirectories);

    private static void AssertLayout(SampleWorkspace workspace)
    {
        Assert.False(File.Exists(Path.Combine(workspace.DirectoryPath, "dotnet-tools.json")));
        Assert.False(Directory.Exists(Path.Combine(workspace.DirectoryPath, ".config")));
        Assert.False(Directory.Exists(Path.Combine(workspace.DirectoryPath, ".claude")));
        Assert.Single(ReferenceDirectories(workspace));
        Assert.Equal(56, Directory.GetFiles(ReferenceDirectories(workspace)[0], "*.md", SearchOption.AllDirectories).Length);
        AssertLinks(workspace, ".agents/skills");
        Assert.Contains("allow_implicit_invocation: false", workspace.Read(".agents/skills/fdg-setup/agents/openai.yaml"));
    }

    private static void AssertLinks(SampleWorkspace workspace, string skills)
    {
        string root = Path.Combine(workspace.DirectoryPath, skills);
        foreach (string file in Directory.GetFiles(root, "SKILL.md", SearchOption.AllDirectories))
        {
            foreach (Match match in Regex.Matches(File.ReadAllText(file), @"\]\(([^)]+\.md)(?:#[^)]*)?\)"))
            {
                Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(file)!, match.Groups[1].Value)), match.Value);
            }
        }
    }

    private static void AssertExistingSettings(string json)
    {
        using JsonDocument parsed = JsonDocument.Parse(json);
        Assert.Equal("yes", parsed.RootElement.GetProperty("env").GetProperty("EXISTING").GetString());
        JsonProperty stop = Assert.Single(parsed.RootElement.GetProperty("hooks").EnumerateObject());
        Assert.Equal("Stop", stop.Name);
        JsonElement group = Assert.Single(stop.Value.EnumerateArray());
        Assert.Equal("echo existing", group.GetProperty("hooks")[0].GetProperty("command").GetString());
    }
}
