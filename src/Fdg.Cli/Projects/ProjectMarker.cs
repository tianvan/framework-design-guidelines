using System.Text.Json;

namespace Fdg.Cli.Projects;

/// <summary>
/// One <c>obj/fdg/marker.json</c> or per-framework <c>marker.&lt;tfm&gt;.json</c>, as the analyzer package writes it
/// before compiling. Values are as written: the profile is not normalized and the language version may be symbolic.
/// </summary>
/// <param name="ProjectPath">The project that wrote it; projects in one directory share <c>obj/</c>.</param>
public sealed record ProjectMarker(
    int? Protocol,
    string? PackageVersion,
    string? Profile,
    string? LangVersion,
    string? TargetFramework,
    string? RulesPath,
    string? ProjectPath)
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// The current markers of the project at <paramref name="projectPath"/>, <c>marker.json</c> first, from whichever
    /// of <paramref name="markerDirectories"/> holds the most recently written ones (see
    /// <see cref="ReadOwn(string, string, out DateTime)"/>). A project that moved to another intermediate directory,
    /// such as <c>UseArtifactsOutput</c>'s, leaves its old markers behind.
    /// </summary>
    public static IReadOnlyList<ProjectMarker> ReadCurrent(IEnumerable<string> markerDirectories, string projectPath)
    {
        IReadOnlyList<ProjectMarker> current = [];
        DateTime currentWritten = DateTime.MinValue;
        foreach (string directory in markerDirectories.Select(Path.GetFullPath).Distinct(ProjectFiles.PathComparer))
        {
            IReadOnlyList<ProjectMarker> markers = ReadOwn(directory, projectPath, out DateTime written);
            if (markers.Count > 0 && (current.Count == 0 || written > currentWritten))
            {
                current = markers;
                currentWritten = written;
            }
        }

        return current;
    }

    /// <summary>
    /// Whether the project at <paramref name="projectPath"/> wrote this marker, by file name so that markers stay
    /// valid when the repository moves. A marker that does not say is taken to be the project's.
    /// </summary>
    private bool BelongsTo(string projectPath) =>
        string.IsNullOrEmpty(ProjectPath) ||
        ProjectFiles.PathComparer.Equals(
            Path.GetFileName(ProjectPath.Replace('\\', '/')),
            Path.GetFileName(projectPath));

    /// <summary>
    /// The markers in <paramref name="markerDirectory"/> that the project at <paramref name="projectPath"/> wrote and
    /// still uses, <c>marker.json</c> first. The package writes markers but never deletes them. A project that targets
    /// one framework now, whose <c>marker.json</c> framework has no <c>marker.&lt;tfm&gt;.json</c>, keeps only
    /// <c>marker.json</c>; the others are left over from when it targeted several. Without <c>marker.json</c>, the
    /// per-framework ones in name order. A marker written by another project in the same directory is skipped.
    /// </summary>
    /// <param name="written">When the newest of the returned markers was written.</param>
    private static IReadOnlyList<ProjectMarker> ReadOwn(string markerDirectory, string projectPath, out DateTime written)
    {
        written = DateTime.MinValue;
        if (!Directory.Exists(markerDirectory))
        {
            return [];
        }

        string mainPath = Path.Combine(markerDirectory, "marker.json");
        ProjectMarker? main = Read(mainPath) is { } read && read.BelongsTo(projectPath) ? read : null;
        (string Path, ProjectMarker Marker)[] perFramework;
        try
        {
            perFramework = Directory.EnumerateFiles(markerDirectory, "marker.*.json")
                .Order(StringComparer.OrdinalIgnoreCase)
                .Select(p => (Path: p, Marker: Read(p)))
                .Where(m => m.Marker is not null && m.Marker.BelongsTo(projectPath))
                .Select(m => (m.Path, m.Marker!))
                .ToArray();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            perFramework = [];
        }

        IReadOnlyList<string> paths;
        IReadOnlyList<ProjectMarker> markers;
        if (main is null)
        {
            paths = perFramework.Select(m => m.Path).ToArray();
            markers = perFramework.Select(m => m.Marker).ToArray();
        }
        else if (perFramework.Any(m => string.Equals(m.Marker.TargetFramework?.Trim(), main.TargetFramework?.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            paths = [mainPath, .. perFramework.Select(m => m.Path)];
            markers = [main, .. perFramework.Select(m => m.Marker)];
        }
        else
        {
            paths = [mainPath];
            markers = [main];
        }

        foreach (string path in paths)
        {
            DateTime time = File.GetLastWriteTimeUtc(path);
            if (time > written)
            {
                written = time;
            }
        }

        return markers;
    }

    /// <returns><see langword="null"/> when the file is missing or unreadable.</returns>
    public static ProjectMarker? Read(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<ProjectMarker>(File.ReadAllText(path), Options);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
