using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Fdg.Cli.Projects;

namespace Fdg.Cli.Hooks;

/// <summary>
/// Whether a project uses the analyzer package (Ruling 50), shared by all hooks and read from files without MSBuild.
/// A project is onboarded when a <c>PackageReference</c> or <c>GlobalPackageReference</c> whose <c>Include</c> or
/// <c>Update</c> is the package id appears in the project file, or in the nearest <c>Directory.Build.props</c>,
/// <c>Directory.Build.targets</c> or <c>Directory.Packages.props</c> at or above it, or in a file one of those imports
/// explicitly (<c>GetPathOfFileAbove</c> or a relative path). Comments never count. When the project has an
/// <c>obj/project.assets.json</c>, it decides whether such a reference took effect. A package brought in only by
/// other imports is not seen.
/// </summary>
public sealed partial class Onboarding
{
    private static readonly string[] ImportedFiles = ["Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props"];

    private static readonly string[] Pathspecs = [":(glob,icase)**/*.csproj"];

    private readonly string _root;
    private readonly Dictionary<string, FileFacts> _files = new(ProjectFiles.PathComparer);

    /// <param name="root">The repository root; files above it are not read.</param>
    public Onboarding(string root)
    {
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
    }

    /// <summary>
    /// Whether any project in the repository is onboarded. Cheap enough for every turn: one <c>git ls-files</c>, and
    /// only project files that mention the package id are parsed.
    /// </summary>
    public static async Task<bool> IsRepositoryOnboardedAsync(GitRepository git, CancellationToken cancellationToken)
    {
        var onboarding = new Onboarding(git.TopLevel);
        foreach (string relativePath in await git.ListFilesAsync(Pathspecs, cancellationToken).ConfigureAwait(false))
        {
            if (!relativePath.Split('/')[..^1].Any(ProjectFiles.IsSkippedDirectory) &&
                onboarding.IsOnboarded(Path.GetFullPath(Path.Combine(git.TopLevel, relativePath))))
            {
                return true;
            }
        }

        return false;
    }

    /// <param name="projectPath">Full path of the .csproj.</param>
    public bool IsOnboarded(string projectPath)
    {
        if (!File.Exists(projectPath))
        {
            return false;
        }

        string directory = Path.GetDirectoryName(projectPath)!;
        bool declared = Facts(projectPath, followImports: false).References ||
            ImportedFiles.Any(name => FindAbove(directory, name) is string file && References(file, new HashSet<string>(ProjectFiles.PathComparer)));
        if (!declared)
        {
            return false;
        }

        string assets = Path.Combine(directory, "obj", "project.assets.json");
        return !File.Exists(assets) || ProjectInfoProvider.ReferencesPackage(assets);
    }

    private bool References(string file, HashSet<string> visited)
    {
        if (!visited.Add(file))
        {
            return false;
        }

        FileFacts facts = Facts(file, followImports: true);
        return facts.References || facts.Imports.Any(import => References(import, visited));
    }

    /// <summary>The nearest file named <paramref name="name"/> at or above <paramref name="directory"/>, up to the root.</summary>
    private string? FindAbove(string directory, string name)
    {
        for (DirectoryInfo? current = new(directory); current is not null; current = current.Parent)
        {
            string candidate = Path.Combine(current.FullName, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            if (ProjectFiles.PathComparer.Equals(Path.TrimEndingDirectorySeparator(current.FullName), _root))
            {
                break;
            }
        }

        return null;
    }

    private FileFacts Facts(string file, bool followImports)
    {
        if (_files.TryGetValue(file, out FileFacts? facts))
        {
            return facts;
        }

        facts = Read(file, followImports);
        _files.Add(file, facts);
        return facts;
    }

    private FileFacts Read(string file, bool followImports)
    {
        XDocument document;
        try
        {
            string text = File.ReadAllText(file);
            bool mentions = text.Contains(ProjectInfoProvider.PackageId, StringComparison.OrdinalIgnoreCase);
            if (!mentions && !(followImports && text.Contains("<Import", StringComparison.Ordinal)))
            {
                // Most project files: nothing to parse.
                return FileFacts.None;
            }

            document = XDocument.Parse(text);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or XmlException)
        {
            return FileFacts.None;
        }

        bool references = document.Descendants()
            .Where(e => e.Name.LocalName is "PackageReference" or "GlobalPackageReference")
            .SelectMany(e => new[] { e.Attribute("Include")?.Value, e.Attribute("Update")?.Value })
            .OfType<string>()
            .SelectMany(v => v.Split(';', StringSplitOptions.TrimEntries))
            .Any(v => v.Equals(ProjectInfoProvider.PackageId, StringComparison.OrdinalIgnoreCase));
        string[] imports = followImports
            ? document.Descendants()
                .Where(e => e.Name.LocalName == "Import" && e.Attribute("Sdk") is null)
                .Select(e => ResolveImport(e.Attribute("Project")?.Value, Path.GetDirectoryName(file)!))
                .OfType<string>()
                .ToArray()
            : [];
        return new FileFacts(references, imports);
    }

    /// <summary>
    /// <c>$([MSBuild]::GetPathOfFileAbove('name', '$(MSBuildThisFileDirectory)../'))</c> or a path relative to the
    /// importing file; anything else that needs MSBuild to evaluate gives <see langword="null"/>.
    /// </summary>
    private string? ResolveImport(string? project, string directory)
    {
        if (string.IsNullOrWhiteSpace(project))
        {
            return null;
        }

        // A placeholder, since the directory itself may hold quotes, commas or parentheses.
        const string ThisDirectory = "\u0001";
        string value = project.Trim().Replace("$(MSBuildThisFileDirectory)", ThisDirectory + "/", StringComparison.OrdinalIgnoreCase);
        Match above = GetPathOfFileAbove().Match(value);
        if (above.Success)
        {
            string start = above.Groups["start"].Success ? above.Groups["start"].Value.Trim() : ThisDirectory;
            string? startDirectory = LocalPath(start, directory);
            return startDirectory is null ? null : FindAbove(startDirectory, above.Groups["name"].Value.Trim());
        }

        string? path = LocalPath(value, directory);
        return path is not null && File.Exists(path) ? path : null;
    }

    private static string? LocalPath(string value, string directory)
    {
        if (value.Contains("$(", StringComparison.Ordinal) || value.Contains("@(", StringComparison.Ordinal) ||
            value.Contains("%(", StringComparison.Ordinal) || value.Contains('*'))
        {
            return null;
        }

        string path = value.Replace("\u0001", directory, StringComparison.Ordinal)
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
        try
        {
            return Path.GetFullPath(path, directory);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"^\$\(\[MSBuild\]::GetPathOfFileAbove\(\s*'?(?<name>[^',)]+)'?\s*(?:,\s*'?(?<start>[^')]*)'?\s*)?\)\)$", RegexOptions.IgnoreCase)]
    private static partial Regex GetPathOfFileAbove();

    /// <param name="Imports">Full paths of the existing files the file imports explicitly.</param>
    private sealed record FileFacts(bool References, IReadOnlyList<string> Imports)
    {
        public static FileFacts None { get; } = new(false, []);
    }
}
