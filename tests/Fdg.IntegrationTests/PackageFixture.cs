using System.Globalization;

namespace Fdg.IntegrationTests;

/// <summary>
/// Packs FrameworkDesignGuidelines.Analyzers once per test run into a temporary feed, with a version that
/// exists nowhere else. Restores go to a per-run packages folder, so the user's global cache never sees it.
/// </summary>
public sealed class PackageFixture : IDisposable
{
    public const string PackageId = "FrameworkDesignGuidelines.Analyzers";

    // Each test class has its own fixture and xunit runs classes in parallel; packs of the same project would
    // write the same obj/ files at once (CS2012).
    private static readonly object PackLock = new();

    private readonly Lazy<string> _fdgTool;

    public PackageFixture()
    {
        RepositoryRoot = FindRepositoryRoot();
        string stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        Version = "0.1.0-test." + stamp;
        Root = Path.Combine(Path.GetTempPath(), "fdg-it-" + stamp + "-" + Guid.NewGuid().ToString("N")[..6]);
        FeedDirectory = Path.Combine(Root, "feed");
        PackagesDirectory = Path.Combine(Root, "packages");
        Directory.CreateDirectory(FeedDirectory);
        Directory.CreateDirectory(PackagesDirectory);
        _fdgTool = new Lazy<string>(InstallFdgTool);

        try
        {
            PackagePath = Pack();
        }
        catch
        {
            // xunit does not dispose a fixture whose constructor throws.
            Dispose();
            throw;
        }
    }

    public string RepositoryRoot { get; }

    public string Version { get; }

    public string Root { get; }

    public string FeedDirectory { get; }

    public string PackagesDirectory { get; }

    public string PackagePath { get; }

    /// <summary>Directory where restore extracts this run's package.</summary>
    public string RestoredPackageDirectory =>
        Path.Combine(PackagesDirectory, PackageId.ToLowerInvariant(), Version.ToLowerInvariant());

    /// <summary>
    /// The dotnet-fdg executable, packed as a tool with this run's version and installed into a tool path under
    /// <see cref="Root"/> on first use.
    /// </summary>
    public string FdgToolPath => _fdgTool.Value;

    public void Dispose()
    {
        TempDirectory.Delete(Root);
    }

    private static DotnetResult PackSerialized(string repositoryRoot, params string[] arguments)
    {
        lock (PackLock)
        {
            return Dotnet.Run(repositoryRoot, packagesDirectory: null, ["pack", .. arguments]);
        }
    }

    private string Pack()
    {
        DotnetResult pack = PackSerialized(
            RepositoryRoot,
            Path.Combine(RepositoryRoot, "src", "FrameworkDesignGuidelines.Package"),
            "-c",
            "Release",
            "-o",
            FeedDirectory,
            "-p:Version=" + Version,
            "-tl:off",
            "-nodeReuse:false");
        if (pack.ExitCode != 0)
        {
            throw new InvalidOperationException("dotnet pack failed:\n" + pack.Output);
        }

        string path = Path.Combine(FeedDirectory, $"{PackageId}.{Version}.nupkg");
        return File.Exists(path)
            ? path
            : throw new InvalidOperationException($"dotnet pack did not produce {path}:\n{pack.Output}");
    }

    private string InstallFdgTool()
    {
        string toolFeed = Path.Combine(Root, "tool-feed");
        string toolDirectory = Path.Combine(Root, "tools");
        DotnetResult pack = PackSerialized(
            RepositoryRoot,
            Path.Combine(RepositoryRoot, "src", "Fdg.Cli"),
            "-c",
            "Release",
            "-o",
            toolFeed,
            "-p:Version=" + Version,
            "-tl:off",
            "-nodeReuse:false");
        if (pack.ExitCode != 0)
        {
            throw new InvalidOperationException("dotnet pack of dotnet-fdg failed:\n" + pack.Output);
        }

        DotnetResult install = Dotnet.Run(
            Root,
            PackagesDirectory,
            "tool",
            "install",
            "dotnet-fdg",
            "--tool-path",
            toolDirectory,
            "--version",
            Version,
            "--add-source",
            toolFeed);
        if (install.ExitCode != 0)
        {
            throw new InvalidOperationException("dotnet tool install of dotnet-fdg failed:\n" + install.Output);
        }

        string executable = Path.Combine(toolDirectory, OperatingSystem.IsWindows() ? "dotnet-fdg.exe" : "dotnet-fdg");
        return File.Exists(executable)
            ? executable
            : throw new InvalidOperationException($"{executable} was not installed:\n{install.Output}");
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FrameworkDesignGuidelines.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("FrameworkDesignGuidelines.slnx not found above " + AppContext.BaseDirectory);
    }
}
