using System.Diagnostics;
using System.Text;

namespace Fdg.Cli.Tests.Hooks;

/// <summary>A real git repository in a <see cref="TempTree"/>, whose path contains a space and CJK characters.</summary>
public sealed class GitTestRepo : IDisposable
{
    public GitTestRepo(bool autoCrlf = false, bool init = true)
    {
        if (init)
        {
            Git("init", "-q", "-b", "main");
            Git("config", "user.name", "FDG Test");
            Git("config", "user.email", "fdg@example.invalid");
            Git("config", "commit.gpgsign", "false");
            Git("config", "core.autocrlf", autoCrlf ? "true" : "false");
        }
    }

    public TempTree Tree { get; } = new();

    public string Root => Tree.Root;

    public string Path(string relativePath) => Tree.Path(relativePath);

    public string Write(string relativePath, string content) => Tree.Write(relativePath, content);

    public string Read(string relativePath) => File.ReadAllText(Path(relativePath));

    /// <summary>Commits everything in the working tree (test setup only; the hooks never commit).</summary>
    public void CommitAll(string message = "commit")
    {
        Git("add", "-A");
        Git("commit", "-q", "-m", message);
    }

    public string Git(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)!;
        Task<string> error = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {error.Result}");
        }

        return output;
    }

    public void Dispose()
    {
        // git makes object files read-only, which Directory.Delete refuses to remove.
        foreach (string file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Tree.Dispose();
    }
}
