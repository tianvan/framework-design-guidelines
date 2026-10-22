using System.Globalization;
using System.Text.Json;
using Fdg.Cli.Tooling;

namespace Fdg.Cli.Projects;

/// <summary>
/// Reads <c>obj/fdg/marker.json</c> (or a per-framework <c>marker.&lt;tfm&gt;.json</c>) written by the analyzer
/// package, and falls back to <c>dotnet msbuild -getProperty</c> when there is no marker, when the project's
/// intermediate directory is not <c>obj/</c>, or to resolve a symbolic language version.
/// </summary>
public sealed class ProjectInfoProvider : IProjectInfoProvider
{
    public const string PackageId = "FrameworkDesignGuidelines.Analyzers";

    private static readonly TimeSpan QueryTimeout = TimeSpan.FromMinutes(2);

    private static readonly string[] Profiles = ["library", "application", "test"];

    private static readonly string[] SymbolicLangVersions = ["latest", "latestmajor", "preview", "default"];

    private static readonly string[] QueriedProperties =
    [
        "ProjectAssetsFile",
        "BaseIntermediateOutputPath",
        "FrameworkDesignGuidelinesProfile",
        "FdgPackageVersion",
        "FdgProtocolVersion",
        "LangVersion",
        "MaxSupportedLangVersion",
        "TargetFramework",
        "TargetFrameworks",
        "NuGetPackageRoot",
    ];

    private readonly IProcessRunner _runner;
    private readonly ILanguageVersionSource _languageVersions;

    public ProjectInfoProvider(IProcessRunner runner, ILanguageVersionSource languageVersions)
    {
        _runner = runner;
        _languageVersions = languageVersions;
    }

    public ProjectInfo Get(string csprojPath)
    {
        if (!SdkStyleDetector.IsSdkStyle(csprojPath))
        {
            return Unknown(csprojPath, ProjectState.NotSdkStyle);
        }

        string projectDirectory = Path.GetDirectoryName(csprojPath)!;
        string intermediateDirectory = Path.Combine(projectDirectory, "obj");
        string assetsFile = Path.Combine(intermediateDirectory, "project.assets.json");
        IReadOnlyDictionary<string, string>? properties = null;
        if (!File.Exists(assetsFile))
        {
            // A custom BaseIntermediateOutputPath, or not restored yet.
            properties = Query(csprojPath, QueriedProperties, targetFramework: null);
            string reportedAssets = Property(properties, "ProjectAssetsFile");
            string reportedIntermediate = Property(properties, "BaseIntermediateOutputPath");
            if (reportedAssets.Length > 0)
            {
                assetsFile = Path.GetFullPath(reportedAssets, projectDirectory);
            }

            if (reportedIntermediate.Length > 0)
            {
                intermediateDirectory = Path.GetFullPath(reportedIntermediate, projectDirectory);
            }

            if (!File.Exists(assetsFile))
            {
                return Unknown(csprojPath, ProjectState.NotRestored);
            }
        }

        if (!ReferencesPackage(assetsFile))
        {
            return Unknown(csprojPath, ProjectState.NoPackage);
        }

        ProjectMarker? marker = ProjectMarker.ReadAll(Path.Combine(intermediateDirectory, "fdg")).FirstOrDefault();
        string? profile;
        string? packageVersion;
        int? protocol;
        string? langVersion;
        string? targetFramework;
        string? rulesPath;
        if (marker is not null)
        {
            profile = marker.Profile;
            packageVersion = marker.PackageVersion;
            protocol = marker.Protocol;
            targetFramework = marker.TargetFramework;
            rulesPath = marker.RulesPath;
            langVersion = ResolveLangVersion(
                csprojPath,
                marker.LangVersion,
                () => QueryMaxSupportedLangVersion(csprojPath, targetFramework));
        }
        else
        {
            properties ??= Query(csprojPath, QueriedProperties, targetFramework: null);
            profile = Property(properties, "FrameworkDesignGuidelinesProfile");
            packageVersion = Property(properties, "FdgPackageVersion");
            protocol = int.TryParse(Property(properties, "FdgProtocolVersion"), NumberStyles.None, CultureInfo.InvariantCulture, out int value)
                ? value
                : null;
            targetFramework = Property(properties, "TargetFramework");
            string targetFrameworks = Property(properties, "TargetFrameworks");
            bool outerBuild = targetFramework.Length == 0 && targetFrameworks.Length > 0;
            if (outerBuild)
            {
                targetFramework = targetFrameworks.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];
            }

            string? firstFramework = targetFramework;
            IReadOnlyDictionary<string, string> evaluated = properties;
            langVersion = ResolveLangVersion(
                csprojPath,
                Property(properties, "LangVersion"),
                // MaxSupportedLangVersion depends on the framework, so a multi-targeting project is asked per framework.
                () => outerBuild
                    ? QueryMaxSupportedLangVersion(csprojPath, firstFramework)
                    : ConcreteLangVersion(Property(evaluated, "MaxSupportedLangVersion")));

            string packageRoot = Property(properties, "NuGetPackageRoot");
            rulesPath = packageRoot.Length > 0 && packageVersion.Length > 0
                ? Path.Combine(packageRoot, PackageId.ToLowerInvariant(), packageVersion.ToLowerInvariant(), "data", "rules.json")
                : null;
        }

        profile = Normalize(profile)?.ToLowerInvariant();
        ProjectState state = profile is not null && Profiles.Contains(profile) ? ProjectState.InScope : ProjectState.NoProfile;
        return new ProjectInfo(
            csprojPath,
            profile,
            Normalize(packageVersion),
            protocol,
            langVersion,
            Normalize(targetFramework),
            Normalize(rulesPath),
            state);
    }

    /// <summary>
    /// A concrete LangVersion is normalized. An empty one means the framework's default, MaxSupportedLangVersion
    /// (the marker has already substituted it). A symbolic one is what the SDK's compiler maps it to, which does not
    /// depend on the framework: netstandard2.0 with <c>latest</c> compiles with the compiler's latest version. When
    /// the compiler cannot tell, the symbolic value is kept rather than guessed.
    /// </summary>
    private string? ResolveLangVersion(string csprojPath, string? value, Func<string?> maxSupportedLangVersion)
    {
        string trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return maxSupportedLangVersion();
        }

        if (ConcreteLangVersion(trimmed) is string concrete)
        {
            return concrete;
        }

        return SymbolicLangVersions.Contains(trimmed.ToLowerInvariant())
            ? _languageVersions.Resolve(Path.GetDirectoryName(csprojPath)!, trimmed) ?? trimmed
            : trimmed;
    }

    /// <summary><c>9</c> becomes <c>9.0</c>; symbolic and unrecognized values give <see langword="null"/>.</summary>
    private static string? ConcreteLangVersion(string? value)
    {
        string trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || SymbolicLangVersions.Contains(trimmed.ToLowerInvariant()))
        {
            return null;
        }

        string[] parts = trimmed.Split('.');
        if (parts.Length > 2 || !parts.All(p => p.Length > 0 && p.All(char.IsAsciiDigit)))
        {
            return null;
        }

        return parts.Length == 1 ? trimmed + ".0" : trimmed;
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Property(IReadOnlyDictionary<string, string> properties, string name) =>
        properties.TryGetValue(name, out string? value) ? value.Trim() : string.Empty;

    private static ProjectInfo Unknown(string csprojPath, ProjectState state) =>
        new(csprojPath, null, null, null, null, null, null, state);

    private static bool ReferencesPackage(string assetsFile)
    {
        try
        {
            using JsonDocument assets = JsonDocument.Parse(File.ReadAllText(assetsFile));
            return assets.RootElement.TryGetProperty("libraries", out JsonElement libraries) &&
                libraries.ValueKind == JsonValueKind.Object &&
                libraries.EnumerateObject().Any(l => l.Name.StartsWith(PackageId + "/", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            return false;
        }
    }

    private string? QueryMaxSupportedLangVersion(string csprojPath, string? targetFramework)
    {
        // Two properties, so that MSBuild answers in JSON.
        IReadOnlyDictionary<string, string> properties =
            Query(csprojPath, ["MaxSupportedLangVersion", "TargetFramework"], targetFramework);
        return ConcreteLangVersion(Property(properties, "MaxSupportedLangVersion"));
    }

    private IReadOnlyDictionary<string, string> Query(
        string csprojPath,
        IReadOnlyList<string> names,
        string? targetFramework)
    {
        // With two or more -getProperty switches MSBuild prints { "Properties": { ... } }.
        List<string> arguments = ["msbuild", csprojPath, .. names.Select(n => "-getProperty:" + n)];
        if (targetFramework is not null)
        {
            arguments.Add("-p:TargetFramework=" + targetFramework);
        }

        ToolResult result = _runner
            .RunAsync("dotnet", arguments, Path.GetDirectoryName(csprojPath)!, QueryTimeout, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        // Evaluation warnings may precede the JSON; it starts on a line of its own.
        int start = result.Output.StartsWith('{') ? 0 : result.Output.IndexOf("\n{", StringComparison.Ordinal) + 1;
        if (result.Succeeded && (start > 0 || result.Output.StartsWith('{')))
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(result.Output[start..]);
                if (document.RootElement.TryGetProperty("Properties", out JsonElement values))
                {
                    return values.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? string.Empty);
                }
            }
            catch (JsonException)
            {
            }
        }

        throw new ProjectEvaluationException(
            result.TimedOut ? "project evaluation timed out" : "project evaluation failed",
            result.Output);
    }
}
