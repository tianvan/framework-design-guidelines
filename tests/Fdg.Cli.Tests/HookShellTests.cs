using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;

namespace Fdg.Cli.Tests;

/// <summary>
/// Runs the plugin's hook wrappers (<c>hooks/fdg-hook.sh</c> and <c>hooks/fdg-hook.ps1</c>) with a restricted
/// <c>PATH</c>, a temporary home directory and a fake <c>dotnet-fdg</c> or <c>dotnet</c>, from a plugin root whose
/// path contains a space and CJK characters.
/// </summary>
public sealed class HookShellTests : IDisposable
{
    private const string InstallHint =
        "{\"systemMessage\":\"FDG: dotnet-fdg is not installed. Run: dotnet tool install -g dotnet-fdg\"}";

    // What an older dotnet-fdg without the hook command writes to stderr.
    private const string UnknownCommandError = "Unrecognized command 'hook'";

    // A stderr line that needs JSON escaping: quotes, backslashes and a tab.
    private const string EscapedError = "A \"quoted\" C:\\sdk\\6.0.999 path\twith a tab";

    // Where `dotnet tool install -g` puts the tool, under the home directory the wrappers are given.
    private static readonly string DotnetToolsDir = Path.Combine("home", ".dotnet", "tools");

    private static readonly string HooksDir = Path.Combine(AppContext.BaseDirectory, "plugin", "hooks");

    // Non-ASCII text, CRLF and LF line breaks and no final line break: the wrapper must not re-encode any of it.
    private static readonly byte[] Payload = Encoding.UTF8.GetBytes(
        "{\"cwd\":\"C:\\\\Users\\\\用户\\\\测试 项目\",\r\n\"prompt\":\"naïve – ✓ 🙂\"\n}");

    // Invalid UTF-8 (0xB4 0xED) between valid text, as a tool might print in a legacy code page.
    private static readonly byte[] NonUtf8Error = Encoding.UTF8.GetBytes("bad ")
        .Concat(new byte[] { 0xB4, 0xED })
        .Concat(Encoding.UTF8.GetBytes(" bytes 中文"))
        .ToArray();

    private readonly TempTree _tree = new();

    private enum FakeBehavior
    {
        // Prints its arguments, echoes stdin byte for byte and exits 1, like `dotnet-fdg hook` on a bad payload.
        Echo,

        // Writes the given stderr bytes and a second line, nothing to stdout, and exits 1.
        Fail,
    }

    public void Dispose()
    {
        _tree.Dispose();
    }

    [BashFact]
    public void Missing_tool_prints_install_hint_json_and_exits_zero()
    {
        AssertInstallHint(RunBash(BashPath(_tree.Directory("empty-path"))));
    }

    [BashFact]
    public void Stdin_is_forwarded_to_dotnet_fdg()
    {
        AssertForwarded(RunBash(BashPath(WriteFakeTool("fake-tool", "dotnet-fdg", FakeBehavior.Echo, forPwsh: false))));
    }

    [BashFact]
    public void Tool_in_dotnet_tools_is_used_when_path_is_stale()
    {
        WriteFakeTool(DotnetToolsDir, "dotnet-fdg", FakeBehavior.Echo, forPwsh: false);

        AssertForwarded(RunBash(BashPath(_tree.Directory("empty-path"))));
    }

    [BashFact]
    public void Wrapper_runs_the_tool_once_without_a_version_probe()
    {
        string dir = WriteFakeTool("fake-tool", "dotnet-fdg", FakeBehavior.Echo, forPwsh: false);

        AssertForwarded(RunBash(BashPath(dir)));
        AssertCalls(dir, "hook session-start");
    }

    [BashFact]
    public void Wrapper_reports_a_tool_failure_without_output()
    {
        string dir = WriteFakeTool(
            "fake-tool", "dotnet-fdg", FakeBehavior.Fail, forPwsh: false, Encoding.UTF8.GetBytes(UnknownCommandError));

        AssertFailure(RunBash(BashPath(dir)), UnknownCommandError);
    }

    [BashFact]
    public void Wrapper_escapes_the_failure_line_for_json()
    {
        string dir = WriteFakeTool(
            "fake-tool", "dotnet-fdg", FakeBehavior.Fail, forPwsh: false, Encoding.UTF8.GetBytes(EscapedError));

        AssertFailure(RunBash(BashPath(dir)), EscapedError);
    }

    [BashFact]
    public void Wrapper_reports_non_utf8_stderr_as_valid_json()
    {
        string dir = WriteFakeTool("fake-tool", "dotnet-fdg", FakeBehavior.Fail, forPwsh: false, NonUtf8Error);

        // iconv drops the invalid bytes; without iconv the wrapper drops every non-ASCII byte.
        string message = AssertFailure(RunBash(BashPath(dir)), expectedLine: null);
        Assert.StartsWith("FDG: dotnet-fdg failed: bad ", message);
        Assert.Contains(" bytes", message);
    }

    [BashFact]
    public void Wrapper_uses_the_local_tool_manifest()
    {
        string dotnet = WriteFakeTool("fake-dotnet", "dotnet", FakeBehavior.Echo, forPwsh: false);
        string tool = WriteFakeTool("fake-tool", "dotnet-fdg", FakeBehavior.Echo, forPwsh: false);
        string repo = WriteManifest("Dotnet-FDG");

        var result = RunBash(BashPath(dotnet + Path.PathSeparator + tool), Path.Combine(repo, "src"), "turn-stop");

        AssertForwarded(result, "tool run dotnet-fdg hook turn-stop");
        AssertCalls(dotnet, "tool run dotnet-fdg hook turn-stop");
        Assert.False(File.Exists(Path.Combine(tool, "calls.txt")));
    }

    [BashFact]
    public void Wrapper_ignores_a_manifest_that_does_not_list_dotnet_fdg()
    {
        // Another package whose command happens to be called dotnet-fdg: only a tools key counts.
        string dotnet = WriteFakeTool("fake-dotnet", "dotnet", FakeBehavior.Echo, forPwsh: false);
        string tool = WriteFakeTool("fake-tool", "dotnet-fdg", FakeBehavior.Echo, forPwsh: false);
        string repo = WriteManifest("dotnet-other");

        AssertForwarded(RunBash(BashPath(dotnet + Path.PathSeparator + tool), repo));
        Assert.False(File.Exists(Path.Combine(dotnet, "calls.txt")));
    }

    [BashFact]
    public void Wrapper_finds_the_tool_under_DOTNET_CLI_HOME()
    {
        WriteFakeTool(Path.Combine("cli home", ".dotnet", "tools"), "dotnet-fdg", FakeBehavior.Echo, forPwsh: false);

        AssertForwarded(RunBash(BashPath(_tree.Directory("empty-path")), dotnetCliHome: _tree.Path("cli home")));
    }

    [PwshFact]
    public void Pwsh_missing_tool_prints_install_hint_json_and_exits_zero()
    {
        AssertInstallHint(RunPwsh(PwshPath(_tree.Directory("empty-path"))));
    }

    [PwshFact]
    public void Pwsh_stdin_is_forwarded_to_dotnet_fdg()
    {
        AssertForwarded(RunPwsh(PwshPath(WriteFakeTool("fake-tool", "dotnet-fdg", FakeBehavior.Echo, forPwsh: true))));
    }

    [PwshFact]
    public void Pwsh_tool_in_dotnet_tools_is_used_when_path_is_stale()
    {
        WriteFakeTool(DotnetToolsDir, "dotnet-fdg", FakeBehavior.Echo, forPwsh: true);

        AssertForwarded(RunPwsh(PwshPath(_tree.Directory("empty-path"))));
    }

    [PwshFact]
    public void Pwsh_wrapper_runs_the_tool_once_without_a_version_probe()
    {
        string dir = WriteFakeTool("fake-tool", "dotnet-fdg", FakeBehavior.Echo, forPwsh: true);

        AssertForwarded(RunPwsh(PwshPath(dir)));
        AssertCalls(dir, "hook session-start");
    }

    [PwshFact]
    public void Pwsh_wrapper_reports_a_tool_failure_without_output()
    {
        string dir = WriteFakeTool(
            "fake-tool", "dotnet-fdg", FakeBehavior.Fail, forPwsh: true, Encoding.UTF8.GetBytes(UnknownCommandError));

        AssertFailure(RunPwsh(PwshPath(dir)), UnknownCommandError);
    }

    [PwshFact]
    public void Pwsh_wrapper_escapes_the_failure_line_for_json()
    {
        string dir = WriteFakeTool(
            "fake-tool", "dotnet-fdg", FakeBehavior.Fail, forPwsh: true, Encoding.UTF8.GetBytes(EscapedError));

        AssertFailure(RunPwsh(PwshPath(dir)), EscapedError);
    }

    [PwshFact]
    public void Pwsh_wrapper_reports_non_utf8_stderr_as_valid_json()
    {
        string dir = WriteFakeTool("fake-tool", "dotnet-fdg", FakeBehavior.Fail, forPwsh: true, NonUtf8Error);

        AssertFailure(RunPwsh(PwshPath(dir)), "bad \uFFFD\uFFFD bytes 中文");
    }

    [PwshFact]
    public void Pwsh_wrapper_uses_the_local_tool_manifest()
    {
        string dotnet = WriteFakeTool("fake-dotnet", "dotnet", FakeBehavior.Echo, forPwsh: true);
        string tool = WriteFakeTool("fake-tool", "dotnet-fdg", FakeBehavior.Echo, forPwsh: true);
        string repo = WriteManifest("Dotnet-FDG");

        var result = RunPwsh(PwshPath(dotnet + Path.PathSeparator + tool), Path.Combine(repo, "src"), "turn-stop");

        AssertForwarded(result, "tool run dotnet-fdg hook turn-stop");
        AssertCalls(dotnet, "tool run dotnet-fdg hook turn-stop");
        Assert.False(File.Exists(Path.Combine(tool, "calls.txt")));
    }

    [PwshFact]
    public void Pwsh_wrapper_finds_the_tool_under_DOTNET_CLI_HOME()
    {
        WriteFakeTool(Path.Combine("cli home", ".dotnet", "tools"), "dotnet-fdg", FakeBehavior.Echo, forPwsh: true);

        AssertForwarded(RunPwsh(PwshPath(_tree.Directory("empty-path")), dotnetCliHome: _tree.Path("cli home")));
    }

    [PwshFact]
    public void Codex_windows_command_survives_special_characters()
    {
        // Codex substitutes nothing in commandWindows any more: the wrapper reads PLUGIN_ROOT, which Codex sets for
        // plugin hooks, and runs the command through the session shell, PowerShell or cmd.exe (docs/hosts.md).
        const string PluginRoot = "a b$c`d'e 中文";
        string command = SingleHook(ReadHooks("hooks.codex.json"), "SessionStart")["commandWindows"]!.GetValue<string>();
        string wrapper = _tree.Path(Path.Combine(PluginRoot, "hooks", "fdg-hook.ps1"));
        Directory.CreateDirectory(Path.GetDirectoryName(wrapper)!);
        File.Copy(Path.Combine(HooksDir, "fdg-hook.ps1"), wrapper);
        string path = PwshPath(WriteFakeTool("fake-tool", "dotnet-fdg", FakeBehavior.Echo, forPwsh: true)) +
            Path.PathSeparator + Path.GetDirectoryName(Shells.Pwsh);
        var environment = new Dictionary<string, string> { ["PLUGIN_ROOT"] = _tree.Path(PluginRoot) };

        AssertForwarded(Run(Shells.Pwsh!, new[] { "-NoProfile", "-Command", command }, path, environment: environment));
        if (OperatingSystem.IsWindows())
        {
            // cmd.exe parses its command line itself, so it gets the raw string rather than ArgumentList's quoting.
            string cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            AssertForwarded(Run(cmd, null, path, environment: environment, rawArguments: $"/d /s /c \"{command}\""));
        }
    }

    [Fact]
    public void Hooks_run_the_wrapper_for_each_event_with_its_timeout()
    {
        var expected = new (string Event, string Argument, int Timeout)[]
        {
            ("SessionStart", "session-start", 15),
            ("UserPromptSubmit", "turn-start", 15),
            ("Stop", "turn-stop", 90),
        };

        JsonObject claude = ReadHooks("hooks.json");
        JsonObject codex = ReadHooks("hooks.codex.json");

        Assert.Equal(expected.Select(e => e.Event), claude.Select(p => p.Key));
        Assert.Equal(expected.Select(e => e.Event), codex.Select(p => p.Key));
        foreach ((string hookEvent, string argument, int timeout) in expected)
        {
            JsonObject claudeHook = SingleHook(claude, hookEvent);
            Assert.Equal(timeout, claudeHook["timeout"]!.GetValue<int>());
            Assert.Equal(
                $"bash \"${{CLAUDE_PLUGIN_ROOT}}/hooks/fdg-hook.sh\" {argument}",
                claudeHook["command"]!.GetValue<string>());

            JsonObject codexHook = SingleHook(codex, hookEvent);
            Assert.Equal(timeout, codexHook["timeout"]!.GetValue<int>());
            Assert.Equal(
                $"bash \"${{PLUGIN_ROOT}}/hooks/fdg-hook.sh\" {argument}",
                codexHook["command"]!.GetValue<string>());

            // The plugin root comes from the environment, so no shell parses it: the text has no `$` or backtick.
            string commandWindows = codexHook["commandWindows"]!.GetValue<string>();
            Assert.Equal(
                "pwsh -NoProfile -ExecutionPolicy Bypass -Command \"& (Join-Path " +
                "([Environment]::GetEnvironmentVariable('PLUGIN_ROOT')) 'hooks/fdg-hook.ps1') " + argument + "\"",
                commandWindows);
            Assert.DoesNotContain('$', commandWindows);
            Assert.DoesNotContain('`', commandWindows);
        }
    }

    [Fact]
    public void Shell_script_has_lf_line_endings()
    {
        // A CRLF checkout (core.autocrlf=true without .gitattributes) breaks bash with "$'\r': command not found".
        Assert.DoesNotContain((byte)'\r', File.ReadAllBytes(Path.Combine(HooksDir, "fdg-hook.sh")));
    }

    private static JsonObject ReadHooks(string fileName)
    {
        JsonNode root = JsonNode.Parse(File.ReadAllText(Path.Combine(HooksDir, fileName)))!;
        return root["hooks"]!.AsObject();
    }

    private static JsonObject SingleHook(JsonObject hooks, string hookEvent)
    {
        // No matcher: SessionStart then also runs on resume, clear and compact.
        JsonObject group = Assert.Single(hooks[hookEvent]!.AsArray())!.AsObject();
        Assert.False(group.ContainsKey("matcher"));
        JsonObject hook = Assert.Single(group["hooks"]!.AsArray())!.AsObject();
        Assert.Equal("command", hook["type"]!.GetValue<string>());
        return hook;
    }

    private static void AssertInstallHint((int ExitCode, byte[] Output, string Error) result)
    {
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(InstallHint, Encoding.UTF8.GetString(result.Output).TrimEnd('\r', '\n'));
    }

    /// <summary>
    /// Asserts one line of strict UTF-8 JSON holding only a <c>systemMessage</c>, and exit code 0. Returns the
    /// message.
    /// </summary>
    private static string AssertFailure((int ExitCode, byte[] Output, string Error) result, string? expectedLine)
    {
        Assert.True(
            result.ExitCode == 0,
            $"Exit code {result.ExitCode}: {Encoding.UTF8.GetString(result.Output)}\n{result.Error}");
        string output = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(result.Output).TrimEnd('\r', '\n');
        Assert.DoesNotContain('\n', output);
        JsonObject json = JsonNode.Parse(output)!.AsObject();
        Assert.Equal(new[] { "systemMessage" }, json.Select(p => p.Key));
        string message = json["systemMessage"]!.GetValue<string>();
        if (expectedLine is not null)
        {
            Assert.Equal("FDG: dotnet-fdg failed: " + expectedLine, message);
        }

        return message;
    }

    private static void AssertForwarded(
        (int ExitCode, byte[] Output, string Error) result, string arguments = "hook session-start")
    {
        // The fake prints its arguments, then echoes stdin, and exits 1: its stdout and exit code pass through.
        byte[] expected = Encoding.UTF8.GetBytes(arguments + "\n").Concat(Payload).ToArray();
        Assert.True(
            expected.SequenceEqual(result.Output),
            $"Output was: {Encoding.UTF8.GetString(result.Output)}\n{result.Error}");
        Assert.Equal(1, result.ExitCode);
    }

    private static void AssertCalls(string fakeDir, params string[] expected)
    {
        string[] calls = File.ReadAllLines(Path.Combine(fakeDir, "calls.txt"));
        Assert.Equal(expected, calls.Select(c => c.TrimEnd()));
    }

    // The fake's cat comes from bash's own directory (Git for Windows' usr/bin, or /usr/bin).
    private static string BashPath(string directory)
    {
        return directory + Path.PathSeparator + Path.GetDirectoryName(Shells.Bash);
    }

    private static string PwshPath(string directory)
    {
        return OperatingSystem.IsWindows() ? directory : BashPath(directory);
    }

    private (int ExitCode, byte[] Output, string Error) RunBash(
        string path, string? workingDirectory = null, string hookEvent = "session-start", string? dotnetCliHome = null)
    {
        string[] arguments = { CopyWrapper("fdg-hook.sh"), hookEvent };
        return Run(Shells.Bash!, arguments, path, workingDirectory, dotnetCliHome);
    }

    private (int ExitCode, byte[] Output, string Error) RunPwsh(
        string path, string? workingDirectory = null, string hookEvent = "session-start", string? dotnetCliHome = null)
    {
        string[] arguments =
        {
            "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", CopyWrapper("fdg-hook.ps1"), hookEvent,
        };
        return Run(Shells.Pwsh!, arguments, path, workingDirectory, dotnetCliHome);
    }

    private string CopyWrapper(string fileName)
    {
        string target = _tree.Path(Path.Combine("plugin root", "hooks", fileName));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(Path.Combine(HooksDir, fileName), target, overwrite: true);
        return target;
    }

    /// <summary>
    /// Writes a repository whose <c>.config/dotnet-tools.json</c> lists the package <paramref name="toolName"/>
    /// with the command <c>dotnet-fdg</c>, and an empty <c>src</c> directory in it. Returns the repository directory.
    /// </summary>
    private string WriteManifest(string toolName)
    {
        _tree.Directory(Path.Combine("repo", "src"));
        _tree.Write(
            Path.Combine("repo", ".config", "dotnet-tools.json"),
            "{\n" +
            "  \"version\": 1,\n" +
            "  \"isRoot\": true,\n" +
            "  \"tools\": {\n" +
            $"    \"{toolName}\": {{\n" +
            "      \"version\": \"1.0.0\",\n" +
            "      \"commands\": [\n" +
            "        \"dotnet-fdg\"\n" +
            "      ],\n" +
            "      \"rollForward\": false\n" +
            "    }\n" +
            "  }\n" +
            "}\n");
        return _tree.Path("repo");
    }

    /// <summary>
    /// Writes a fake tool called <paramref name="name"/> into <paramref name="relativeDir"/>. Each run appends its
    /// arguments to <c>calls.txt</c> next to it, then behaves as <paramref name="behavior"/> says, writing
    /// <paramref name="error"/> to stderr for <see cref="FakeBehavior.Fail"/>. Returns its directory.
    /// </summary>
    private string WriteFakeTool(
        string relativeDir, string name, FakeBehavior behavior, bool forPwsh, byte[]? error = null)
    {
        string dir = _tree.Directory(relativeDir);
        byte[] stderr = (error ?? Array.Empty<byte>()).Concat(Encoding.UTF8.GetBytes("\nsecond line\n")).ToArray();
        if (forPwsh && OperatingSystem.IsWindows())
        {
            // PowerShell only runs PATHEXT commands on Windows: a batch file hands over to a script. cmd.exe reads
            // batch files in the OEM code page, so the CJK path is only reached through %~dp0.
            string run = behavior == FakeBehavior.Echo
                ? "$header = [Text.Encoding]::UTF8.GetBytes(($args -join ' ') + \"`n\")\n" +
                  "$out.Write($header, 0, $header.Length)\n" +
                  "[Console]::OpenStandardInput().CopyTo($out)\n"
                : $"$bytes = [byte[]]({string.Join(',', stderr.Select(b => "0x" + b.ToString("X2")))})\n" +
                  "$err = [Console]::OpenStandardError()\n" +
                  "$err.Write($bytes, 0, $bytes.Length)\n" +
                  "$err.Flush()\n";
            _tree.Write(
                Path.Combine(relativeDir, "fake.ps1"),
                "[IO.File]::AppendAllText((Join-Path $PSScriptRoot 'calls.txt'), ($args -join ' ') + \"`n\")\n" +
                "$out = [Console]::OpenStandardOutput()\n" +
                run +
                "$out.Flush()\n" +
                "exit 1\n");
            _tree.Write(
                Path.Combine(relativeDir, name + ".cmd"),
                "@echo off\r\n" +
                $"\"{Shells.Pwsh}\" -NoProfile -ExecutionPolicy Bypass -File \"%~dp0fake.ps1\" %*\r\n");
            return dir;
        }

        string body = behavior == FakeBehavior.Echo
            ? "printf '%s\\n' \"$*\"\ncat\n"
            : $"printf '{string.Concat(stderr.Select(b => "\\" + Convert.ToString(b, 8).PadLeft(3, '0')))}' >&2\n";
        _tree.Write(
            Path.Combine(relativeDir, name),
            "#!/bin/sh\n" +
            "printf '%s\\n' \"$*\" >> \"$(dirname \"$0\")/calls.txt\"\n" +
            body +
            "exit 1\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                Path.Combine(dir, name),
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return dir;
    }

    private (int ExitCode, byte[] Output, string Error) Run(
        string fileName,
        IEnumerable<string>? arguments,
        string path,
        string? workingDirectory = null,
        string? dotnetCliHome = null,
        IDictionary<string, string>? environment = null,
        string? rawArguments = null)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,

            // Outside any repository, so no tool manifest is found unless a test writes one.
            WorkingDirectory = workingDirectory ?? _tree.Directory("cwd"),
        };
        if (rawArguments is not null)
        {
            startInfo.Arguments = rawArguments;
        }

        foreach (string argument in arguments ?? Array.Empty<string>())
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["PATH"] = path;
        startInfo.Environment["HOME"] = _tree.Directory("home");
        startInfo.Environment["USERPROFILE"] = _tree.Directory("home");
        startInfo.Environment.Remove("DOTNET_CLI_HOME");
        startInfo.Environment.Remove("PLUGIN_ROOT");
        if (dotnetCliHome is not null)
        {
            startInfo.Environment["DOTNET_CLI_HOME"] = dotnetCliHome;
        }

        foreach (KeyValuePair<string, string> variable in environment ?? new Dictionary<string, string>())
        {
            startInfo.Environment[variable.Key] = variable.Value;
        }

        using Process process = Process.Start(startInfo)!;
        Task<string> error = process.StandardError.ReadToEndAsync();
        var output = new MemoryStream();
        Task copy = process.StandardOutput.BaseStream.CopyToAsync(output);
        try
        {
            process.StandardInput.BaseStream.Write(Payload);
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // The wrapper may exit without reading stdin when the tool is missing.
        }

        Assert.True(process.WaitForExit(60_000), "The wrapper did not exit within 60 seconds.");
        copy.Wait();
        return (process.ExitCode, output.ToArray(), error.Result);
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
