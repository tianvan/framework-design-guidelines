using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Fdg.Cli.Hooks;

/// <summary>
/// Read-only git access for the hooks: <c>rev-parse</c>, <c>status</c>, <c>cat-file</c> and <c>diff --no-index</c>.
/// Nothing here touches the index, HEAD or the working tree. Output is read as raw bytes, so line endings and
/// NUL separators arrive intact.
/// </summary>
public sealed class GitRepository
{
    private GitRepository(string topLevel, string gitDirectory)
    {
        TopLevel = topLevel;
        GitDirectory = gitDirectory;
    }

    /// <summary>The root of the working tree, as a full path.</summary>
    public string TopLevel { get; }

    /// <summary>The repository's git directory (<c>.git</c>, or a worktree's own directory), as a full path.</summary>
    public string GitDirectory { get; }

    /// <returns><see langword="null"/> when <paramref name="directory"/> is not inside a git working tree.</returns>
    public static async Task<GitRepository?> OpenAsync(string directory, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directory))
        {
            return null;
        }

        GitResult result;
        try
        {
            result = await RunAsync(
                directory,
                ["rev-parse", "--show-toplevel", "--absolute-git-dir"],
                cancellationToken).ConfigureAwait(false);
        }
        catch (Win32Exception)
        {
            // git is not installed.
            return null;
        }

        string[] lines = result.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (result.ExitCode != 0 || lines.Length != 2)
        {
            return null;
        }

        return new GitRepository(Path.GetFullPath(lines[0]), Path.GetFullPath(lines[1]));
    }

    /// <returns>The commit HEAD points to, or <see langword="null"/> in a repository without commits.</returns>
    public async Task<string?> HeadAsync(CancellationToken cancellationToken)
    {
        GitResult result = await RunAsync(TopLevel, ["rev-parse", "--verify", "--quiet", "HEAD"], cancellationToken)
            .ConfigureAwait(false);
        string head = result.Text.Trim();
        return result.ExitCode == 0 && head.Length > 0 ? head : null;
    }

    /// <summary>Changed, staged and untracked files from <c>git status --porcelain -z</c>.</summary>
    public async Task<IReadOnlyList<GitStatusEntry>> StatusAsync(CancellationToken cancellationToken)
    {
        GitResult result = await RunAsync(
            TopLevel,
            [
                "--no-optional-locks",
                "status",
                "--porcelain=v1",
                "-z",
                "--untracked-files=all",
                "--no-renames",
                "--ignore-submodules=all",
            ],
            cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException("git status failed: " + result.Error.Trim());
        }

        return GitStatusEntry.Parse(result.Text);
    }

    /// <summary>
    /// Tracked and untracked, not ignored files matching <paramref name="pathspecs"/>
    /// (<c>git ls-files -co --exclude-standard</c>), each once.
    /// </summary>
    /// <returns>Paths relative to <see cref="TopLevel"/>, with forward slashes.</returns>
    public async Task<IReadOnlyList<string>> ListFilesAsync(IReadOnlyList<string> pathspecs, CancellationToken cancellationToken)
    {
        GitResult result = await RunAsync(
            TopLevel,
            ["ls-files", "-co", "--exclude-standard", "-z", "--", .. pathspecs],
            cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException("git ls-files failed: " + result.Error.Trim());
        }

        // An unmerged file is listed once per stage.
        return result.Text.Split('\0', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// The file as a checkout would write it (<c>git cat-file --filters</c>), so <c>core.autocrlf</c> and
    /// <c>.gitattributes</c> conversions match the working tree.
    /// </summary>
    /// <param name="relativePath">Relative to <see cref="TopLevel"/>, with forward slashes.</param>
    /// <returns><see langword="null"/> when the commit has no such file.</returns>
    public async Task<byte[]?> ReadCommittedFileAsync(string commit, string relativePath, CancellationToken cancellationToken)
    {
        GitResult result = await RunAsync(
            TopLevel,
            ["cat-file", "--filters", commit + ":" + relativePath],
            cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0 ? result.Output : null;
    }

    /// <summary><c>git diff --no-index -U0</c> between two files; the output is empty when they are equal.</summary>
    /// <param name="ignoreWhitespace">Ignore whitespace-only changes, line ending changes included.</param>
    public async Task<string> DiffFilesAsync(
        string oldPath,
        string newPath,
        bool ignoreWhitespace,
        CancellationToken cancellationToken)
    {
        // --inter-hunk-context=0 overrides diff.interHunkContext, which would merge hunks over unchanged lines.
        List<string> arguments =
            ["diff", "--no-index", "--no-color", "--no-ext-diff", "--no-textconv", "-U0", "--inter-hunk-context=0"];
        if (ignoreWhitespace)
        {
            arguments.Add("--ignore-all-space");
            arguments.Add("--ignore-cr-at-eol");
        }

        arguments.AddRange(["--", oldPath, newPath]);
        GitResult result = await RunAsync(TopLevel, arguments, cancellationToken).ConfigureAwait(false);
        // 1 means the files differ.
        if (result.ExitCode is not (0 or 1))
        {
            throw new InvalidOperationException("git diff failed: " + result.Error.Trim());
        }

        return result.Text;
    }

    private static async Task<GitResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardErrorEncoding = Encoding.UTF8,
        };
        // Unquoted UTF-8 paths in the output.
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("core.quotePath=false");
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        process.StandardInput.Close();
        using var output = new MemoryStream();
        Task copyOutput = process.StandardOutput.BaseStream.CopyToAsync(output, cancellationToken);
        Task<string> readError = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await Task.WhenAll(copyOutput, readError).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        return new GitResult(process.ExitCode, output.ToArray(), await readError.ConfigureAwait(false));
    }

    private sealed record GitResult(int ExitCode, byte[] Output, string Error)
    {
        public string Text => Encoding.UTF8.GetString(Output);
    }
}
