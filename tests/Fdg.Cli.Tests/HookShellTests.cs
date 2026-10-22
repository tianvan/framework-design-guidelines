using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;

namespace Fdg.Cli.Tests;

/// <summary>
/// Runs the plugin's hook wrappers (<c>hooks/fdg-hook.sh</c> and <c>hooks/fdg-hook.ps1</c>) with a restricted
/// <c>PATH</c> and a fake <c>dotnet</c>, from a plugin root whose path contains a space and CJK characters.
/// </summary>
public sealed class HookShellTests : IDisposable
{
    private const string InstallHint =
        "{\"systemMessage\":\"FDG: dotnet-fdg is not installed. Run: dotnet tool install -g dotnet-fdg\"}";

    private static readonly string HooksDir = Path.Combine(AppContext.BaseDirectory, "plugin", "hooks");

    // Non-ASCII text, CRLF and LF line breaks and no final line break: the wrapper must not re-encode any of it.
    private static readonly byte[] Payload = Encoding.UTF8.GetBytes(
        "{\"cwd\":\"C:\\\\Users\\\\用户\\\\测试 项目\",\r\n\"prompt\":\"naïve – ✓ 🙂\"\n}");

    private readonly TempTree _tree = new();

    public void Dispose()
    {
        _tree.Dispose();
    }

    [BashFact]
    public void Missing_tool_prints_install_hint_json_and_exits_zero()
    {
        AssertInstallHint(RunBash(_tree.Directory("empty-path")));
    }

    [BashFact]
    public void Failing_version_check_prints_install_hint()
    {
        AssertInstallHint(RunBash(WriteFakeDotnet(versionExitCode: 1, forPwsh: false)));
    }

    [BashFact]
    public void Stdin_is_forwarded_to_dotnet_fdg()
    {
        string fakeDir = WriteFakeDotnet(versionExitCode: 0, forPwsh: false);

        // The fake's cat comes from bash's own directory (Git for Windows' usr/bin, or /usr/bin).
        var result = RunBash(fakeDir + Path.PathSeparator + Path.GetDirectoryName(Shells.Bash));

        AssertForwarded(result);
    }

    [PwshFact]
    public void Pwsh_missing_tool_prints_install_hint_json_and_exits_zero()
    {
        AssertInstallHint(RunPwsh(_tree.Directory("empty-path")));
    }

    [PwshFact]
    public void Pwsh_failing_version_check_prints_install_hint()
    {
        AssertInstallHint(RunPwsh(WriteFakeDotnet(versionExitCode: 1, forPwsh: true)));
    }

    [PwshFact]
    public void Pwsh_stdin_is_forwarded_to_dotnet_fdg()
    {
        string fakeDir = WriteFakeDotnet(versionExitCode: 0, forPwsh: true);
        string path = OperatingSystem.IsWindows()
            ? fakeDir
            : fakeDir + Path.PathSeparator + Path.GetDirectoryName(Shells.Bash);

        AssertForwarded(RunPwsh(path));
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
            Assert.Equal(
                $"pwsh -NoProfile -ExecutionPolicy Bypass -File \"${{PLUGIN_ROOT}}/hooks/fdg-hook.ps1\" {argument}",
                codexHook["commandWindows"]!.GetValue<string>());
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

    private static void AssertForwarded((int ExitCode, byte[] Output, string Error) result)
    {
        // The fake prints its arguments, then echoes stdin, and exits 1 like `dotnet fdg hook` on a bad payload.
        byte[] expected = Encoding.UTF8.GetBytes("fdg hook session-start\n").Concat(Payload).ToArray();
        Assert.True(expected.SequenceEqual(result.Output), $"Output was: {Encoding.UTF8.GetString(result.Output)}\n{result.Error}");
        Assert.Equal(1, result.ExitCode);
    }

    private (int ExitCode, byte[] Output, string Error) RunBash(string path)
    {
        return Run(Shells.Bash!, new[] { CopyWrapper("fdg-hook.sh"), "session-start" }, path);
    }

    private (int ExitCode, byte[] Output, string Error) RunPwsh(string path)
    {
        string[] arguments = { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", CopyWrapper("fdg-hook.ps1"), "session-start" };
        return Run(Shells.Pwsh!, arguments, path);
    }

    private string CopyWrapper(string fileName)
    {
        string target = _tree.Path(Path.Combine("plugin root", "hooks", fileName));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(Path.Combine(HooksDir, fileName), target, overwrite: true);
        return target;
    }

    /// <summary>
    /// Writes a fake <c>dotnet</c> that answers <c>fdg --version</c> with <paramref name="versionExitCode"/>; for
    /// anything else it prints its arguments, echoes stdin byte for byte and exits 1. Returns its directory.
    /// </summary>
    private string WriteFakeDotnet(int versionExitCode, bool forPwsh)
    {
        if (forPwsh && OperatingSystem.IsWindows())
        {
            // PowerShell only runs PATHEXT commands on Windows: a batch file hands over to an echo script. cmd.exe
            // reads batch files in the OEM code page, so the CJK path is only reached through %~dp0.
            _tree.Write(
                Path.Combine("fake-dotnet", "echo.ps1"),
                "$out = [Console]::OpenStandardOutput()\n" +
                "$header = [Text.Encoding]::UTF8.GetBytes(($args -join ' ') + \"`n\")\n" +
                "$out.Write($header, 0, $header.Length)\n" +
                "[Console]::OpenStandardInput().CopyTo($out)\n" +
                "$out.Flush()\n" +
                "exit 1\n");
            _tree.Write(
                Path.Combine("fake-dotnet", "dotnet.cmd"),
                "@echo off\r\n" +
                $"if \"%~1 %~2\"==\"fdg --version\" exit /b {versionExitCode}\r\n" +
                $"\"{Shells.Pwsh}\" -NoProfile -ExecutionPolicy Bypass -File \"%~dp0echo.ps1\" %*\r\n");
            return _tree.Path("fake-dotnet");
        }

        string dir = _tree.Directory("fake-dotnet");
        _tree.Write(
            Path.Combine("fake-dotnet", "dotnet"),
            "#!/bin/sh\n" +
            $"if [ \"$1 $2\" = \"fdg --version\" ]; then exit {versionExitCode}; fi\n" +
            "printf '%s\\n' \"$*\"\n" +
            "cat\n" +
            "exit 1\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                Path.Combine(dir, "dotnet"),
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return dir;
    }

    private static (int ExitCode, byte[] Output, string Error) Run(string fileName, IEnumerable<string> arguments, string path)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["PATH"] = path;

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
