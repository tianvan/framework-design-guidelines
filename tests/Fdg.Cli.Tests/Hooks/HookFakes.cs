using System.Text.Json.Nodes;
using Fdg.Cli.Projects;
using Fdg.Cli.Tooling;

namespace Fdg.Cli.Tests.Hooks;

/// <summary>Projects whose state the test sets; any other project is reported as having no profile.</summary>
public sealed class FakeHookProjects : IProjectInfoProvider
{
    private readonly Dictionary<string, ProjectInfo> _infos = new(StringComparer.OrdinalIgnoreCase);

    public Exception? Failure { get; set; }

    public void Add(string csprojPath, ProjectState state, string profile = "application") =>
        _infos[csprojPath] = new ProjectInfo(csprojPath, profile, "0.1.0", 1, "7.3", "netstandard2.0", null, state);

    public ProjectInfo Get(string csprojPath)
    {
        if (Failure is not null)
        {
            throw Failure;
        }

        return _infos.TryGetValue(csprojPath, out ProjectInfo? info)
            ? info
            : new ProjectInfo(csprojPath, null, null, null, null, null, null, ProjectState.NoProfile);
    }
}

public sealed record HookFormatCall(string ProjectPath, IReadOnlyList<string>? IncludeFiles, IReadOnlyList<string> ExcludeDiagnostics);

/// <summary>Stands in for dotnet format; <see cref="OnFormat"/> can edit the files like the real one.</summary>
public sealed class FakeHookFormatter : IFormatRunner
{
    private readonly object _lock = new();

    public List<HookFormatCall> Calls { get; } = [];

    public Action<string> OnFormat { get; set; } = _ => { };

    public ToolResult Result { get; set; } = new(0, string.Empty, false);

    public Task<ToolResult> FormatAsync(
        string projectPath,
        IReadOnlyList<string>? includeFiles,
        IReadOnlyList<string> excludeDiagnostics,
        CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            Calls.Add(new HookFormatCall(projectPath, includeFiles, excludeDiagnostics));
        }

        OnFormat(projectPath);
        return Task.FromResult(Result);
    }
}

public sealed record HookBuildCall(string ProjectPath, string SarifDirectory, bool NoIncremental);

/// <summary>Stands in for dotnet build: writes the SARIF log <see cref="Sarif"/> returns for the call, if any.</summary>
public sealed class FakeHookBuilder : IBuildRunner
{
    public List<HookBuildCall> Calls { get; } = [];

    /// <summary>The results to log for a call, or <see langword="null"/> to write no SARIF file.</summary>
    public Func<HookBuildCall, IReadOnlyList<SarifResult>?> Sarif { get; set; } = _ => [];

    public ToolResult Result { get; set; } = new(0, string.Empty, false);

    public TimeSpan Delay { get; set; } = TimeSpan.Zero;

    public async Task<ToolResult> BuildAsync(
        string projectPath,
        string sarifDirectory,
        bool noIncremental,
        CancellationToken cancellationToken)
    {
        var call = new HookBuildCall(projectPath, sarifDirectory, noIncremental);
        Calls.Add(call);
        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, cancellationToken);
        }

        IReadOnlyList<SarifResult>? results = Sarif(call);
        if (results is not null)
        {
            Directory.CreateDirectory(sarifDirectory);
            File.WriteAllText(Path.Combine(sarifDirectory, "netstandard2.0.sarif"), SarifResult.Log(results));
        }

        return Result;
    }
}

/// <summary>A hand-written SARIF 2.1 result as Roslyn's error logger writes it.</summary>
/// <param name="Uri">Defaults to the file URI of <paramref name="FilePath"/>.</param>
public sealed record SarifResult(string RuleId, string Level, string Message, string FilePath, int Line, string? Uri = null)
{
    /// <summary>Suppressed in source, for example by <c>#pragma warning disable</c>.</summary>
    public bool Suppressed { get; init; }

    public static string Log(IEnumerable<SarifResult> results)
    {
        var array = new JsonArray();
        foreach (SarifResult r in results)
        {
            var result = new JsonObject
            {
                ["ruleId"] = r.RuleId,
                ["level"] = r.Level,
                ["message"] = new JsonObject { ["text"] = r.Message },
                ["locations"] = new JsonArray(new JsonObject
                {
                    ["physicalLocation"] = new JsonObject
                    {
                        ["artifactLocation"] = new JsonObject { ["uri"] = r.Uri ?? new Uri(r.FilePath).AbsoluteUri },
                        ["region"] = new JsonObject { ["startLine"] = r.Line, ["startColumn"] = 1, ["endLine"] = r.Line, ["endColumn"] = 2 },
                    },
                }),
            };
            if (r.Suppressed)
            {
                result["suppressions"] = new JsonArray(new JsonObject { ["kind"] = "inSource" });
            }

            array.Add(result);
        }

        var log = new JsonObject
        {
            ["$schema"] = "http://json.schemastore.org/sarif-2.1.0",
            ["version"] = "2.1.0",
            ["runs"] = new JsonArray(new JsonObject
            {
                ["results"] = array,
                ["tool"] = new JsonObject { ["driver"] = new JsonObject { ["name"] = "Microsoft (R) Visual C# Compiler" } },
            }),
        };
        return log.ToJsonString();
    }
}
