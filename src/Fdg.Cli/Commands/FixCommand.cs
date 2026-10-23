using System.Security.Cryptography;
using Fdg.Cli.Projects;
using Fdg.Cli.Rules;
using Fdg.Cli.Tooling;

namespace Fdg.Cli.Commands;

/// <summary>
/// <c>dotnet fdg fix [&lt;path&gt;...]</c>: runs dotnet format on every in-scope project, fixing the manifest's
/// registered diagnostics except the breaking ones. Prints one line per project; exits with 1 when any project failed, 2 on invalid arguments.
/// </summary>
public sealed class FixCommand
{
    /// <summary>The newest marker and rules.json protocol this CLI understands.</summary>
    public const int SupportedProtocol = 1;

    private const int MaxDetailLines = 20;

    private readonly IProjectInfoProvider _projects;
    private readonly IRestoreRunner _restorer;
    private readonly IFormatRunner _formatter;
    private readonly TextWriter _output;
    private readonly TextWriter _error;

    public FixCommand(
        IProjectInfoProvider projects,
        IRestoreRunner restorer,
        IFormatRunner formatter,
        TextWriter output,
        TextWriter error)
    {
        _projects = projects;
        _restorer = restorer;
        _formatter = formatter;
        _output = output;
        _error = error;
    }

    public async Task<int> RunAsync(IReadOnlyList<string> paths, string cwd, CancellationToken cancellationToken)
    {
        IReadOnlyList<FixTarget> targets;
        try
        {
            targets = PathResolver.Resolve(paths, cwd);
        }
        catch (CommandException e)
        {
            _error.WriteLine("dotnet fdg fix: " + e.Message);
            return 2;
        }

        if (targets.Count == 0)
        {
            _output.WriteLine("no C# projects found");
            return 0;
        }

        bool failed = false;
        foreach (FixTarget target in targets)
        {
            failed |= !await FixAsync(target, cwd, cancellationToken).ConfigureAwait(false);
        }

        return failed ? 1 : 0;
    }

    /// <returns><see langword="false"/> when the project failed; skipping an out-of-scope project is not a failure.</returns>
    private async Task<bool> FixAsync(FixTarget target, string cwd, CancellationToken cancellationToken)
    {
        string display = DisplayPath(target.ProjectPath, cwd);
        if (!File.Exists(target.ProjectPath))
        {
            // A solution can list a project that has since been deleted or moved.
            _output.WriteLine($"skipped {display}: project file not found");
            return false;
        }

        ProjectInfo info;
        try
        {
            info = _projects.Get(target.ProjectPath);
            if (info.State == ProjectState.NotRestored)
            {
                ToolResult restore = await _restorer.RestoreAsync(target.ProjectPath, cancellationToken).ConfigureAwait(false);
                if (!restore.Succeeded)
                {
                    Report("skipped", display, restore.TimedOut ? "restore timed out" : "restore failed", restore);
                    return false;
                }

                info = _projects.Get(target.ProjectPath);
            }
        }
        catch (ProjectEvaluationException e)
        {
            Report("skipped", display, e.Message, new ToolResult(1, e.Output, false));
            return false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _output.WriteLine($"skipped {display}: cannot read the project file: {e.Message}");
            return false;
        }

        if (info.State != ProjectState.InScope)
        {
            _output.WriteLine($"skipped {display}: {SkipReason(info)}");
            return info.State != ProjectState.NotRestored;
        }

        if (info.Protocol > SupportedProtocol)
        {
            _output.WriteLine(
                $"skipped {display}: package protocol {info.Protocol} is newer than this dotnet-fdg supports " +
                $"({SupportedProtocol}); update it with: dotnet tool update -g dotnet-fdg");
            return false;
        }

        IReadOnlyList<string> diagnostics;
        try
        {
            diagnostics = RulesManifest.LoadOrEmbedded(info.RulesPath).FixDiagnostics;
        }
        catch (NotSupportedException e)
        {
            _output.WriteLine($"skipped {display}: {e.Message}");
            return false;
        }

        IReadOnlyList<string>? includes = target.IncludeFiles is null
            ? null
            : FormatIncludes.ToProjectRelative(target.ProjectPath, target.IncludeFiles);
        ToolResult format = await FormatPasses
            .RunAsync(_formatter, target.ProjectPath, includes, diagnostics, () => HashSources(target), cancellationToken)
            .ConfigureAwait(false);
        if (!format.Succeeded)
        {
            string reason = format.TimedOut
                ? "dotnet format timed out"
                : $"dotnet format exited with code {format.ExitCode}";
            Report("failed", display, reason, format);
            return false;
        }

        _output.WriteLine("fixed " + display);
        return true;
    }

    private static string SkipReason(ProjectInfo info) => info.State switch
    {
        ProjectState.NotSdkStyle => "not an SDK-style project",
        ProjectState.NoPackage => ProjectInfoProvider.PackageId + " is not referenced",
        ProjectState.NoProfile when info.Profile is null => "FrameworkDesignGuidelinesProfile is not set",
        ProjectState.NoProfile => $"unknown FrameworkDesignGuidelinesProfile '{info.Profile}'",
        ProjectState.NotRestored => "restore did not produce project.assets.json",
        _ => info.State.ToString(),
    };

    /// <summary>Relative to the current directory with forward slashes, as the user typed it.</summary>
    private static string DisplayPath(string path, string cwd) => Path.GetRelativePath(cwd, path).Replace('\\', '/');

    /// <summary>A hash over the paths and contents of the C# files a format pass may change.</summary>
    private static string HashSources(FixTarget target)
    {
        IEnumerable<string> roots = target.IncludeFiles ?? [Path.GetDirectoryName(target.ProjectPath)!];
        IEnumerable<string> files = roots
            .SelectMany(root => Directory.Exists(root) ? ProjectFiles.Enumerate(root, "*.cs") : [root])
            .Where(File.Exists)
            .Distinct(ProjectFiles.PathComparer);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string file in files)
        {
            hash.AppendData(System.Text.Encoding.UTF8.GetBytes(file + "\n"));
            hash.AppendData(SHA256.HashData(File.ReadAllBytes(file)));
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private void Report(string verb, string display, string reason, ToolResult result)
    {
        _output.WriteLine($"{verb} {display}: {reason}");
        foreach (string line in result.FirstLines(MaxDetailLines))
        {
            _output.WriteLine("  " + line.TrimEnd());
        }
    }
}
