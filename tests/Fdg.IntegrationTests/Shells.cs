namespace Fdg.IntegrationTests;

/// <summary>Locates the shells that run the hook wrappers on this machine.</summary>
internal static class Shells
{
    public static string? Bash { get; } = FindBash();

    public static string? Pwsh { get; } = FindOnPath(OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh");

    private static string? FindBash()
    {
        if (!OperatingSystem.IsWindows())
        {
            return FindOnPath("bash");
        }

        // Not System32\bash.exe, which starts WSL: the Git Bash that Claude Code uses, found from git.exe.
        string? git = FindOnPath("git.exe");
        for (DirectoryInfo? directory = git is null ? null : new FileInfo(git).Directory;
            directory is not null;
            directory = directory.Parent)
        {
            string bash = Path.Combine(directory.FullName, "usr", "bin", "bash.exe");
            if (File.Exists(bash))
            {
                return bash;
            }
        }

        return null;
    }

    private static string? FindOnPath(string fileName)
    {
        return (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory, fileName))
            .FirstOrDefault(File.Exists);
    }
}

public sealed class BashFactAttribute : FactAttribute
{
    public BashFactAttribute()
    {
        if (Shells.Bash is null)
        {
            Skip = "bash is not available.";
        }
    }
}

public sealed class PwshFactAttribute : FactAttribute
{
    public PwshFactAttribute()
    {
        if (Shells.Pwsh is null)
        {
            Skip = "pwsh is not available.";
        }
    }
}
