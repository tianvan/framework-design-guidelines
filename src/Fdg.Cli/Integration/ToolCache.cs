using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Fdg.Cli.IO;
using Fdg.Cli.Tooling;

namespace Fdg.Cli.Integration;

internal static partial class ToolCache
{
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(5);

    public static bool IsVersion(string? version) => version is { Length: > 0 and <= 128 } && ExactVersion().IsMatch(version);

    public static async Task PrepareAsync(string worktree, string version, CancellationToken cancellationToken)
    {
        if (!IsVersion(version))
        {
            throw new CommandException($"Expected an exact FDG NuGet version, got '{version}'.");
        }

        string folder = WorktreePath.Resolve(PlatformDirectory());
        string target = Path.Combine(folder, version.ToLowerInvariant());
        string executable = OperatingSystem.IsWindows() ? "dotnet-fdg.exe" : "dotnet-fdg";
        if (WorktreePath.Contains(worktree, folder) || !WorktreePath.IsSafe(folder, Path.Combine(target, executable)) ||
            !WorktreePath.IsSafe(folder, target + ".lock") || !WorktreePath.IsSafe(folder, target + ".tmp"))
        {
            throw new CommandException("The FDG tool cache must be outside the current worktree and its version entries cannot be symbolic links.");
        }

        Directory.CreateDirectory(folder);
        using FileStream lease = await FileLease.AcquireAsync(target + ".lock", cancellationToken).ConfigureAwait(false);
        var runner = new ProcessRunner();
        if (await IsReadyAsync(target).ConfigureAwait(false))
        {
            return;
        }

        // A fixed staging directory is safe under the version lease and can be cleaned on the next attempt.
        string staging = target + ".tmp";
        DeleteDirectory(staging);
        try
        {
            ToolResult install = await runner.RunAsync(
                "dotnet", ["tool", "install", "dotnet-fdg", "--version", version, "--tool-path", staging],
                worktree, InstallTimeout, cancellationToken).ConfigureAwait(false);
            if (!install.Succeeded)
            {
                throw new CommandException($"Could not prepare dotnet-fdg {version}. Check the repository's NuGet sources and retry.\n{install.Output.TrimEnd()}");
            }

            if (!await IsReadyAsync(staging).ConfigureAwait(false))
            {
                throw new CommandException($"dotnet-fdg {version} could not run after installation. Check that the .NET SDK and runtime are available.");
            }

            DeleteDirectory(target);
            Directory.Move(staging, target);
        }
        catch (Win32Exception e)
        {
            throw new CommandException($"Could not run .NET to prepare dotnet-fdg {version}: {e.Message}");
        }
        finally
        {
            DeleteDirectory(staging);
        }

        async Task<bool> IsReadyAsync(string directory)
        {
            string tool = Path.Combine(directory, executable);
            if (!File.Exists(tool))
            {
                return false;
            }

            try
            {
                ToolResult query = await runner.RunAsync(tool, ["--version"], worktree, TimeSpan.FromSeconds(10), cancellationToken)
                    .ConfigureAwait(false);
                return query.Succeeded && query.Output.Trim().Equals(version, StringComparison.OrdinalIgnoreCase);
            }
            catch (Win32Exception)
            {
                return false;
            }
        }
    }

    private static string PlatformDirectory()
    {
        string? path = Environment.GetEnvironmentVariable("FDG_TOOL_CACHE");
        if (string.IsNullOrEmpty(path))
        {
            if (OperatingSystem.IsWindows())
            {
                path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FDG", "tools");
            }
            else
            {
                string? xdg = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
                string userCache = !string.IsNullOrEmpty(xdg) && Path.IsPathFullyQualified(xdg)
                    ? xdg
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
                path = Path.Combine(userCache, "fdg", "tools");
            }
        }

        if (!Path.IsPathFullyQualified(path))
        {
            throw new CommandException("FDG_TOOL_CACHE must be an absolute path.");
        }

        string platform = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
        // The apphost the active SDK creates must match the architecture that runs FDG, including emulated SDKs.
        return Path.Combine(Path.GetFullPath(path), platform + "-" + RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant());
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    [GeneratedRegex(@"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)*)?\z", RegexOptions.CultureInvariant)]
    private static partial Regex ExactVersion();
}
