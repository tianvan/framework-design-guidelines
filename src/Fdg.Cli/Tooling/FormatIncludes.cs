namespace Fdg.Cli.Tooling;

/// <summary>Converts include paths to the form <c>dotnet format --include</c> matches.</summary>
public static class FormatIncludes
{
    /// <summary>
    /// Makes each full path relative to the project directory with forward slashes; directories get a trailing
    /// <c>/</c>, without which dotnet format does not match the files below them.
    /// </summary>
    public static IReadOnlyList<string> ToProjectRelative(string projectPath, IReadOnlyList<string> fullPaths)
    {
        string projectDirectory = Path.GetDirectoryName(projectPath)!;
        return fullPaths
            .Select(path =>
            {
                string relative = Path.GetRelativePath(projectDirectory, path).Replace('\\', '/');
                return Directory.Exists(path) && !relative.EndsWith('/') ? relative + "/" : relative;
            })
            .ToArray();
    }
}
