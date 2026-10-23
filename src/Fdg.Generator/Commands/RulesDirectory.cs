namespace Fdg.Generator.Commands;

public static class RulesDirectory
{
    /// <summary>
    /// Uses <paramref name="explicitPath"/> when given; otherwise the nearest <c>rules/meta.yaml</c>
    /// at or above the current directory.
    /// </summary>
    public static bool TryResolve(string? explicitPath, out string rulesDir, out string error)
    {
        error = string.Empty;
        if (explicitPath is not null)
        {
            // Drops the trailing separator of `--rules rules/`, so that the parent is the repository root.
            rulesDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(explicitPath));
            if (!File.Exists(Path.Combine(rulesDir, "meta.yaml")))
            {
                error = $"{Path.Combine(rulesDir, "meta.yaml")} was not found.";
                return false;
            }

            return true;
        }

        for (DirectoryInfo? directory = new(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "rules");
            if (File.Exists(Path.Combine(candidate, "meta.yaml")))
            {
                rulesDir = candidate;
                return true;
            }
        }

        rulesDir = string.Empty;
        error = "rules/meta.yaml was not found above the current directory; pass --rules <dir>.";
        return false;
    }
}
