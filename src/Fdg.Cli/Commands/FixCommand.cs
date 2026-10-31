using System.Security.Cryptography;
using Fdg.Cli.Projects;
using Fdg.Cli.Rules;
using Fdg.Cli.Tooling;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Fdg.Cli.Commands;

/// <summary>
/// <c>dotnet fdg fix [&lt;path&gt;...]</c>: runs dotnet format on every in-scope project, fixing the manifest's
/// registered diagnostics except the breaking ones. Prints one line per project; exits with 1 when any project failed, 2 on invalid arguments.
/// A file with syntax errors is left out of the run and gets a <c>skipped</c> line of its own; that is not a failure.
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

        string[] sources = SourceFiles(target);
        IReadOnlyList<string>? includes = target.IncludeFiles is null
            ? null
            : FormatIncludes.ToProjectRelative(target.ProjectPath, target.IncludeFiles);
        IReadOnlyList<string> excludes = [];
        (string File, int Line)[] broken = sources
            .Select(file => (File: file, Line: FirstSyntaxErrorLine(file) ?? 0))
            .Where(file => file.Line > 0)
            .ToArray();
        if (broken.Length > 0)
        {
            // dotnet format still reformats the whitespace of a file it cannot parse (measured with SDK 10.0.403),
            // so such files are left out of the run.
            foreach ((string file, int line) in broken)
            {
                _output.WriteLine(
                    $"skipped {DisplayPath(file, cwd)}: it has syntax errors (first at line {line}); " +
                    "dotnet format does not change it");
            }

            sources = sources.Except(broken.Select(b => b.File), ProjectFiles.PathComparer).ToArray();
            if (sources.Length == 0)
            {
                return true;
            }

            (includes, excludes) = LeaveOut(target, broken.Select(b => b.File).ToHashSet(ProjectFiles.PathComparer), sources);
        }

        ToolResult format = await FormatPasses
            .RunAsync(
                _formatter,
                target.ProjectPath,
                includes,
                excludes,
                diagnostics,
                sources,
                () => HashSources(sources),
                cancellationToken)
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

    /// <summary>
    /// The includes and excludes that leave the broken files out. A broken file the user named is dropped from the
    /// includes; the others, in the whole project or under a named directory, are excluded, so the run keeps its
    /// scope, files linked from outside the project directory included. Excludes go to every batch of a run, so when
    /// they would take up half of the command line, the remaining files are included one by one instead.
    /// </summary>
    private static (IReadOnlyList<string>? Includes, IReadOnlyList<string> Excludes) LeaveOut(
        FixTarget target,
        IReadOnlySet<string> broken,
        IReadOnlyList<string> remaining)
    {
        string[] excluded = broken
            .Where(file => target.IncludeFiles is null || !target.IncludeFiles.Contains(file, ProjectFiles.PathComparer))
            .Order(StringComparer.Ordinal)
            .ToArray();
        IReadOnlyList<string> excludes = FormatIncludes.ToProjectRelative(target.ProjectPath, excluded);
        if (DotnetFormatRunner.ArgumentsLength(excludes) > DotnetFormatRunner.MaxCommandLineLength / 2)
        {
            return (FormatIncludes.ToProjectRelative(target.ProjectPath, remaining), []);
        }

        IReadOnlyList<string>? includes = target.IncludeFiles is null
            ? null
            : FormatIncludes.ToProjectRelative(target.ProjectPath, target.IncludeFiles.Where(file => !broken.Contains(file)).ToArray());
        return (includes, excludes);
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

    /// <summary>The files a format pass may change: the included files, or the project's C# files.</summary>
    private static string[] SourceFiles(FixTarget target)
    {
        IEnumerable<string> roots = target.IncludeFiles ?? [Path.GetDirectoryName(target.ProjectPath)!];
        return roots
            .SelectMany(root => Directory.Exists(root) ? ProjectFiles.Enumerate(root, "*.cs") : [root])
            .Where(File.Exists)
            .Distinct(ProjectFiles.PathComparer)
            .ToArray();
    }

    /// <summary>
    /// A hash over the paths and contents of the C# files a format pass may change. A file deleted during the run
    /// counts as empty instead of failing the run.
    /// </summary>
    private static string HashSources(IEnumerable<string> files)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string file in files)
        {
            hash.AppendData(System.Text.Encoding.UTF8.GetBytes(file + "\n"));
            hash.AppendData(SHA256.HashData(ReadOrEmpty(file)));
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static byte[] ReadOrEmpty(string file)
    {
        try
        {
            return File.ReadAllBytes(file);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return [];
        }
    }

    /// <summary>
    /// The 1-based line of the first syntax error in the file, parsed with the newest language version so that only
    /// real syntax errors count; <see langword="null"/> when it has none or cannot be read.
    /// </summary>
    /// <remarks>
    /// The file is parsed on its own, without the project's preprocessor symbols: code under <c>#if DEBUG</c> or
    /// another symbol the project defines is inactive here, so an error there is missed, and an <c>#else</c> branch
    /// the project never compiles is parsed, so an error there skips a file dotnet format would have changed. Every
    /// <c>fix</c> parses each file it may change once, a cost that grows with the size of the project.
    /// </remarks>
    private static int? FirstSyntaxErrorLine(string file)
    {
        string text;
        try
        {
            text = File.ReadAllText(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        SyntaxTree tree = CSharpSyntaxTree.ParseText(text, new CSharpParseOptions(LanguageVersion.Preview));
        Diagnostic? first = tree.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .MinBy(d => d.Location.SourceSpan.Start);
        return first is null ? null : first.Location.GetLineSpan().StartLinePosition.Line + 1;
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
