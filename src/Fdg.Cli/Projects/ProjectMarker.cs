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
    /// The readable markers in <paramref name="markerDirectory"/>: <c>marker.json</c> first, then the per-framework
    /// ones in name order. Read lazily, so taking the first reads only what it needs.
    /// </summary>
    public static IEnumerable<ProjectMarker> ReadAll(string markerDirectory)
    {
        if (!Directory.Exists(markerDirectory))
        {
            yield break;
        }

        IEnumerable<string> candidates = Directory.EnumerateFiles(markerDirectory, "marker.*.json")
            .Order(StringComparer.OrdinalIgnoreCase)
            .Prepend(Path.Combine(markerDirectory, "marker.json"));
        foreach (string path in candidates)
        {
            if (Read(path) is ProjectMarker marker)
            {
                yield return marker;
            }
        }
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
