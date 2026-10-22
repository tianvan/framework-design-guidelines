using System.Security;

namespace Fdg.IntegrationTests;

/// <summary>
/// A throwaway directory, outside the repository, with its own nuget.config pointing at the test feed and
/// empty Directory.Build/Packages files so nothing from parent directories leaks in. Its name contains a
/// space and CJK characters, as real repositories do.
/// </summary>
public sealed class SampleWorkspace : IDisposable
{
    private readonly PackageFixture _fixture;

    public SampleWorkspace(PackageFixture fixture, string name, string editorConfigSections = "")
    {
        _fixture = fixture;
        DirectoryPath = Path.Combine(fixture.Root, "cases", $"{name} 测试 {Guid.NewGuid().ToString("N")[..6]}");
        Directory.CreateDirectory(DirectoryPath);

        File.Copy(Path.Combine(fixture.RepositoryRoot, "global.json"), Path.Combine(DirectoryPath, "global.json"));
        Write("Directory.Build.props", "<Project />\n");
        Write("Directory.Build.targets", "<Project />\n");
        Write("Directory.Packages.props", "<Project />\n");
        Write(".editorconfig", "root = true\n" + editorConfigSections);
        Write(
            "nuget.config",
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <config>
                <add key="globalPackagesFolder" value="{SecurityElement.Escape(fixture.PackagesDirectory)}" />
              </config>
              <packageSources>
                <clear />
                <add key="fdg-test" value="{SecurityElement.Escape(fixture.FeedDirectory)}" />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
            </configuration>
            """);
    }

    public string DirectoryPath { get; }

    /// <summary>The PackageReference to this run's package, without PrivateAssets, as a plain consumer would write it.</summary>
    public string PackageReference =>
        $"""<PackageReference Include="{PackageFixture.PackageId}" Version="{_fixture.Version}" />""";

    /// <summary>Writes a netstandard2.0, C# 7.3 project referencing the package and returns the csproj path.</summary>
    public string AddProject(string name, string? profile, string source, string extraProperties = "")
    {
        string profileProperty = profile is null
            ? string.Empty
            : $"<FrameworkDesignGuidelinesProfile>{profile}</FrameworkDesignGuidelinesProfile>";
        return AddProjectFromXml(
            name,
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>netstandard2.0</TargetFramework>
                <LangVersion>7.3</LangVersion>
                {profileProperty}
                {extraProperties}
              </PropertyGroup>
              <ItemGroup>
                {PackageReference}
              </ItemGroup>
            </Project>
            """,
            source);
    }

    /// <summary>Writes a project from the full csproj text and returns the csproj path.</summary>
    public string AddProjectFromXml(string name, string projectXml, string source)
    {
        Write(Path.Combine(name, name + ".csproj"), projectXml);
        Write(Path.Combine(name, name + ".cs"), source);
        return Path.Combine(DirectoryPath, name, name + ".csproj");
    }

    public void Write(string relativePath, string content)
    {
        string path = Path.Combine(DirectoryPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content.ReplaceLineEndings("\n"));
    }

    public string Read(string relativePath) => File.ReadAllText(Path.Combine(DirectoryPath, relativePath));

    public DotnetResult Dotnet(params string[] arguments) =>
        IntegrationTests.Dotnet.Run(DirectoryPath, _fixture.PackagesDirectory, arguments);

    /// <summary>Runs the installed <c>dotnet-fdg</c> tool in the workspace directory.</summary>
    public DotnetResult Fdg(params string[] arguments) =>
        IntegrationTests.Dotnet.RunProgram(_fixture.FdgToolPath, DirectoryPath, _fixture.PackagesDirectory, arguments);

    /// <summary>Runs a <c>dotnet fdg hook</c> entry point with the given JSON on stdin.</summary>
    public DotnetResult FdgHook(string hookEvent, string json, IReadOnlyDictionary<string, string> environment) =>
        IntegrationTests.Dotnet.RunProgram(
            _fixture.FdgToolPath,
            DirectoryPath,
            _fixture.PackagesDirectory,
            json,
            environment,
            ["hook", hookEvent]);

    /// <summary>Builds without the compiler server, so no process keeps the package's analyzers locked.</summary>
    public DotnetResult Build(string projectPath, params string[] extraArguments) =>
        Dotnet(["build", projectPath, "-tl:off", "-nodeReuse:false", "-p:UseSharedCompilation=false", .. extraArguments]);

    public void Dispose()
    {
        TempDirectory.Delete(DirectoryPath);
    }
}
