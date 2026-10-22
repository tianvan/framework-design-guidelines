using System.Globalization;
using Fdg.Cli.Tooling;

namespace Fdg.Cli.Projects;

/// <summary>Turns a symbolic <c>LangVersion</c> (latest, latestMajor, default, preview) into a concrete version.</summary>
public interface ILanguageVersionSource
{
    /// <returns>The concrete version, or <see langword="null"/> when it cannot be determined.</returns>
    string? Resolve(string projectDirectory, string symbolicLangVersion);
}

/// <summary>
/// Asks the compiler of the SDK a project builds with: <c>dotnet exec &lt;sdk&gt;/Roslyn/bincore/csc.dll
/// -langversion:?</c>. Symbolic values depend on the compiler, not on the target framework, so
/// MaxSupportedLangVersion is the answer only for an empty LangVersion. Results are cached per SDK directory.
/// </summary>
public sealed class CompilerLanguageVersions : ILanguageVersionSource
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(1);

    private readonly IProcessRunner _runner;
    private readonly Dictionary<string, SupportedLanguageVersions?> _bySdk = new(StringComparer.OrdinalIgnoreCase);

    public CompilerLanguageVersions(IProcessRunner runner)
    {
        _runner = runner;
    }

    public string? Resolve(string projectDirectory, string symbolicLangVersion)
    {
        string? sdkDirectory = FindSdkDirectory(projectDirectory);
        if (sdkDirectory is null)
        {
            return null;
        }

        if (!_bySdk.TryGetValue(sdkDirectory, out SupportedLanguageVersions? versions))
        {
            string compiler = Path.Combine(sdkDirectory, "Roslyn", "bincore", "csc.dll");
            ToolResult result = File.Exists(compiler)
                ? Run(["exec", compiler, "-langversion:?"], projectDirectory)
                : new ToolResult(1, string.Empty, false);
            versions = result.Succeeded ? SupportedLanguageVersions.Parse(result.Output) : null;
            _bySdk.Add(sdkDirectory, versions);
        }

        return versions?.Resolve(symbolicLangVersion);
    }

    /// <summary>The SDK that <c>dotnet</c> selects in the project directory (global.json applies there).</summary>
    private string? FindSdkDirectory(string projectDirectory)
    {
        ToolResult version = Run(["--version"], projectDirectory);
        ToolResult list = Run(["--list-sdks"], projectDirectory);
        if (!version.Succeeded || !list.Succeeded)
        {
            return null;
        }

        string selected = version.Output.Trim();
        foreach (string line in list.Output.ReplaceLineEndings("\n").Split('\n'))
        {
            // 10.0.402 [C:\Program Files\dotnet\sdk]
            int open = line.IndexOf(" [", StringComparison.Ordinal);
            if (open > 0 && line.TrimEnd().EndsWith(']') && line[..open] == selected)
            {
                string root = line[(open + 2)..line.TrimEnd().LastIndexOf(']')];
                return Path.Combine(root, selected);
            }
        }

        return null;
    }

    private ToolResult Run(IReadOnlyList<string> arguments, string workingDirectory) =>
        _runner.RunAsync("dotnet", arguments, workingDirectory, Timeout, CancellationToken.None).GetAwaiter().GetResult();
}

/// <summary>What a compiler's <c>-langversion:?</c> lists, reduced to the values the symbolic names stand for.</summary>
/// <param name="Latest">The highest released version, minor versions included.</param>
/// <param name="LatestMajor">The highest released <c>x.0</c> version; also what <c>default</c> means.</param>
/// <param name="Preview">The highest listed version, preview versions included.</param>
public sealed record SupportedLanguageVersions(string Latest, string LatestMajor, string Preview)
{
    /// <returns><see langword="null"/> when the output lists no numeric version.</returns>
    public static SupportedLanguageVersions? Parse(string output)
    {
        var versions = new List<(Version Version, bool IsPreview)>();
        foreach (string raw in output.ReplaceLineEndings("\n").Split('\n'))
        {
            // "7.3", "14.0 (default)"; a version only a preview compiler offers is annotated with "preview".
            string line = raw.Trim();
            int space = line.IndexOf(' ');
            string number = space < 0 ? line : line[..space];
            if (number.Length == 0 || !number.All(c => char.IsAsciiDigit(c) || c == '.'))
            {
                continue;
            }

            if (Version.TryParse(number.Contains('.') ? number : number + ".0", out Version? version))
            {
                bool isPreview = space >= 0 && line[space..].Contains("preview", StringComparison.OrdinalIgnoreCase);
                versions.Add((version, isPreview));
            }
        }

        Version[] released = versions.Where(v => !v.IsPreview).Select(v => v.Version).ToArray();
        if (released.Length == 0)
        {
            return null;
        }

        return new SupportedLanguageVersions(
            Format(released.Max()!),
            Format(released.Where(v => v.Minor == 0).DefaultIfEmpty(released.Max()!).Max()!),
            Format(versions.Max(v => v.Version)!));
    }

    public string? Resolve(string symbolicLangVersion) => symbolicLangVersion.Trim().ToLowerInvariant() switch
    {
        "latest" => Latest,
        "latestmajor" or "default" => LatestMajor,
        "preview" => Preview,
        _ => null,
    };

    private static string Format(Version version) =>
        version.Major.ToString(CultureInfo.InvariantCulture) + "." + version.Minor.ToString(CultureInfo.InvariantCulture);
}
