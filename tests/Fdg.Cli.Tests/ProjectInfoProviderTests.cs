using Fdg.Cli.Projects;

namespace Fdg.Cli.Tests;

public sealed class ProjectInfoProviderTests : IDisposable
{
    private const string Assets =
        """{ "version": 3, "libraries": { "FrameworkDesignGuidelines.Analyzers/0.1.0": { "type": "package" } } }""";

    private const string AssetsWithoutPackage = """{ "version": 3, "libraries": {} }""";

    private static readonly ILanguageVersionSource NoCompiler = new FakeLanguageVersions(null);

    private readonly TempTree _tree = new();

    public void Dispose()
    {
        _tree.Dispose();
    }

    [Fact]
    public void Legacy_project_is_detected()
    {
        string legacy = _tree.Write(
            "Legacy/Legacy.csproj",
            """
            <?xml version="1.0" encoding="utf-8"?>
            <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <Import Project="$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props" />
            </Project>
            """);

        Assert.False(SdkStyleDetector.IsSdkStyle(legacy));
    }

    [Theory]
    [InlineData("<Project Sdk=\"Microsoft.NET.Sdk\" />")]
    [InlineData("<Project><Sdk Name=\"Microsoft.NET.Sdk\" /></Project>")]
    [InlineData("<Project><Import Project=\"Sdk.props\" Sdk=\"Microsoft.NET.Sdk\" /></Project>")]
    public void Sdk_style_project_forms_are_detected(string xml)
    {
        Assert.True(SdkStyleDetector.IsSdkStyle(_tree.Write("A/A.csproj", xml)));
    }

    [Fact]
    public void Legacy_project_is_NotSdkStyle()
    {
        string legacy = _tree.Write("Legacy/Legacy.csproj", "<Project ToolsVersion=\"15.0\" />");
        var runner = new FakeProcessRunner();

        ProjectInfo info = new ProjectInfoProvider(runner, NoCompiler).Get(legacy);

        Assert.Equal(ProjectState.NotSdkStyle, info.State);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public void Marker_takes_precedence_over_msbuild_query()
    {
        string project = WriteRestoredProject();
        _tree.Write("A/obj/fdg/marker.json", Marker("library", "7.3", "netstandard2.0", @"C:\\packages\\rules.json"));
        var runner = new FakeProcessRunner().RespondToMSBuild(new Dictionary<string, string>
        {
            ["FrameworkDesignGuidelinesProfile"] = "test",
            ["FdgPackageVersion"] = "9.9.9",
            ["FdgProtocolVersion"] = "9",
        });

        ProjectInfo info = new ProjectInfoProvider(runner, NoCompiler).Get(project);

        Assert.Equal(
            new ProjectInfo(
                project,
                "library",
                "0.1.0",
                1,
                "7.3",
                "netstandard2.0",
                @"C:\packages\rules.json",
                ProjectState.InScope),
            info);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public void Per_framework_marker_is_used_when_marker_json_is_missing()
    {
        string project = WriteRestoredProject();
        _tree.Write("A/obj/fdg/marker.net8.0.json", Marker("application", "12.0", "net8.0"));

        ProjectInfo info = new ProjectInfoProvider(new FakeProcessRunner(), NoCompiler).Get(project);

        Assert.Equal(ProjectState.InScope, info.State);
        Assert.Equal("net8.0", info.TargetFramework);
        Assert.Equal("12.0", info.LangVersion);
        Assert.Null(info.RulesPath);
    }

    [Theory]
    [InlineData("latest", "14.0")]
    [InlineData("latestMajor", "14.0")]
    [InlineData("default", "14.0")]
    [InlineData("preview", "15.0")]
    public void Symbolic_lang_version_is_resolved_from_the_compiler_not_the_framework(string symbolic, string expected)
    {
        // netstandard2.0 defaults to 7.3, but LangVersion=latest compiles with the SDK compiler's latest version.
        string project = WriteRestoredProject();
        _tree.Write("A/obj/fdg/marker.json", Marker("test", symbolic, "netstandard2.0"));
        var runner = new FakeProcessRunner().RespondToMSBuild(new Dictionary<string, string>
        {
            ["LangVersion"] = symbolic,
            ["MaxSupportedLangVersion"] = "7.3",
        });
        var compiler = new FakeLanguageVersions(new SupportedLanguageVersions("14.0", "14.0", "15.0"));

        ProjectInfo info = new ProjectInfoProvider(runner, compiler).Get(project);

        Assert.Equal(expected, info.LangVersion);
        Assert.Equal([(_tree.Path("A"), symbolic)], compiler.Calls);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public void Symbolic_lang_version_without_marker_is_resolved_from_the_compiler()
    {
        string project = WriteRestoredProject();
        var runner = new FakeProcessRunner().RespondToMSBuild(new Dictionary<string, string>
        {
            ["ProjectAssetsFile"] = _tree.Path("A/obj/project.assets.json"),
            ["FrameworkDesignGuidelinesProfile"] = "library",
            ["FdgPackageVersion"] = "0.1.0",
            ["FdgProtocolVersion"] = "1",
            ["LangVersion"] = "latest",
            ["MaxSupportedLangVersion"] = "7.3",
            ["TargetFramework"] = "netstandard2.0",
        });
        var compiler = new FakeLanguageVersions(new SupportedLanguageVersions("14.0", "14.0", "14.0"));

        ProjectInfo info = new ProjectInfoProvider(runner, compiler).Get(project);

        Assert.Equal("14.0", info.LangVersion);
    }

    [Fact]
    public void Symbolic_lang_version_is_kept_when_the_compiler_cannot_tell()
    {
        string project = WriteRestoredProject();
        _tree.Write("A/obj/fdg/marker.json", Marker("test", "latest", "netstandard2.0"));

        ProjectInfo info = new ProjectInfoProvider(new FakeProcessRunner(), new FakeLanguageVersions(null)).Get(project);

        Assert.Equal("latest", info.LangVersion);
    }

    [Fact]
    public void Integer_lang_version_is_written_with_minor_version()
    {
        string project = WriteRestoredProject();
        _tree.Write("A/obj/fdg/marker.json", Marker("test", "9", "net8.0"));

        ProjectInfo info = new ProjectInfoProvider(new FakeProcessRunner(), NoCompiler).Get(project);

        Assert.Equal("9.0", info.LangVersion);
    }

    [Fact]
    public void Missing_assets_file_is_NotRestored()
    {
        string project = _tree.Write("A/A.csproj", TempTree.SdkProject);
        var runner = new FakeProcessRunner().RespondToMSBuild(new Dictionary<string, string>
        {
            ["ProjectAssetsFile"] = _tree.Path("A/obj/project.assets.json"),
            ["BaseIntermediateOutputPath"] = "obj\\",
        });

        ProjectInfo info = new ProjectInfoProvider(runner, NoCompiler).Get(project);

        Assert.Equal(ProjectState.NotRestored, info.State);
    }

    [Fact]
    public void Msbuild_query_is_used_without_marker()
    {
        string project = WriteRestoredProject();
        var runner = new FakeProcessRunner().RespondToMSBuild(new Dictionary<string, string>
        {
            ["ProjectAssetsFile"] = _tree.Path("A/obj/project.assets.json"),
            ["BaseIntermediateOutputPath"] = "obj\\",
            ["FrameworkDesignGuidelinesProfile"] = " Application ",
            ["FdgPackageVersion"] = "0.1.0",
            ["FdgProtocolVersion"] = "1",
            ["LangVersion"] = "",
            ["MaxSupportedLangVersion"] = "7.3",
            ["TargetFramework"] = "",
            ["TargetFrameworks"] = "netstandard2.0;net8.0",
            ["NuGetPackageRoot"] = "",
        });

        ProjectInfo info = new ProjectInfoProvider(runner, NoCompiler).Get(project);

        Assert.Equal(
            new ProjectInfo(project, "application", "0.1.0", 1, "7.3", "netstandard2.0", null, ProjectState.InScope),
            info);
    }

    [Fact]
    public void Custom_base_intermediate_output_path_is_found_through_msbuild()
    {
        string project = _tree.Write("A/A.csproj", TempTree.SdkProject);
        _tree.Write("artifacts/A/project.assets.json", Assets);
        _tree.Write("artifacts/A/fdg/marker.json", Marker("library", "8.0", "netstandard2.1"));
        var runner = new FakeProcessRunner().RespondToMSBuild(new Dictionary<string, string>
        {
            ["ProjectAssetsFile"] = _tree.Path("artifacts/A/project.assets.json"),
            ["BaseIntermediateOutputPath"] = "..\\artifacts\\A\\",
        });

        ProjectInfo info = new ProjectInfoProvider(runner, NoCompiler).Get(project);

        Assert.Equal(ProjectState.InScope, info.State);
        Assert.Equal("library", info.Profile);
        Assert.Equal("netstandard2.1", info.TargetFramework);
    }

    [Fact]
    public void Project_without_package_is_NoPackage_even_with_a_stale_marker()
    {
        string project = WriteRestoredProject(AssetsWithoutPackage);
        _tree.Write("A/obj/fdg/marker.json", Marker("library", "7.3", "netstandard2.0"));

        ProjectInfo info = new ProjectInfoProvider(new FakeProcessRunner(), NoCompiler).Get(project);

        Assert.Equal(ProjectState.NoPackage, info.State);
    }

    [Theory]
    [InlineData("")]
    [InlineData("libary")]
    public void Missing_or_unknown_profile_is_NoProfile(string profile)
    {
        string project = WriteRestoredProject();
        _tree.Write("A/obj/fdg/marker.json", Marker(profile, "7.3", "netstandard2.0"));

        ProjectInfo info = new ProjectInfoProvider(new FakeProcessRunner(), NoCompiler).Get(project);

        Assert.Equal(ProjectState.NoProfile, info.State);
        Assert.Equal(profile.Length == 0 ? null : profile, info.Profile);
    }

    [Fact]
    public void Msbuild_warnings_before_the_json_are_ignored()
    {
        string project = WriteRestoredProject();
        string json = System.Text.Json.JsonSerializer.Serialize(new
        {
            Properties = new Dictionary<string, string>
            {
                ["FrameworkDesignGuidelinesProfile"] = "test",
                ["FdgPackageVersion"] = "0.1.0",
                ["LangVersion"] = "8.0",
            },
        });
        var runner = new FakeProcessRunner().Respond(
            _ => true,
            new Cli.Tooling.ToolResult(0, "warning MSB4011: \"{x}\" cannot be imported again.\n" + json + "\n", false));

        ProjectInfo info = new ProjectInfoProvider(runner, NoCompiler).Get(project);

        Assert.Equal(ProjectState.InScope, info.State);
        Assert.Equal("8.0", info.LangVersion);
    }

    [Fact]
    public void Failed_msbuild_query_throws_with_its_output()
    {
        string project = _tree.Write("A/A.csproj", TempTree.SdkProject);
        var runner = new FakeProcessRunner()
            .Respond(_ => true, new Cli.Tooling.ToolResult(1, "error MSB4025: The project file could not be loaded.", false));

        var exception = Assert.Throws<ProjectEvaluationException>(() => new ProjectInfoProvider(runner, NoCompiler).Get(project));

        Assert.Contains("MSB4025", exception.Output);
    }

    private sealed class FakeLanguageVersions(SupportedLanguageVersions? versions) : ILanguageVersionSource
    {
        public List<(string ProjectDirectory, string Symbolic)> Calls { get; } = [];

        public string? Resolve(string projectDirectory, string symbolicLangVersion)
        {
            Calls.Add((projectDirectory, symbolicLangVersion));
            return versions?.Resolve(symbolicLangVersion);
        }
    }

    private static string Marker(string profile, string langVersion, string targetFramework, string rulesPath = "") =>
        $$"""
        { "protocol": 1, "packageVersion": "0.1.0", "profile": "{{profile}}", "langVersion": "{{langVersion}}", "targetFramework": "{{targetFramework}}", "rulesPath": "{{rulesPath}}", "projectPath": "x" }
        """;

    private string WriteRestoredProject(string assets = Assets)
    {
        string project = _tree.Write("A/A.csproj", TempTree.SdkProject);
        _tree.Write("A/obj/project.assets.json", assets);
        return project;
    }
}
