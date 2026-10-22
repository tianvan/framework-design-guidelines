using System.Text.Json;

namespace Fdg.IntegrationTests;

/// <summary>Packs the analyzer package and uses it from throwaway netstandard2.0 / C# 7.3 projects.</summary>
[Trait("Category", "Integration")]
public sealed class PackageTests : IClassFixture<PackageFixture>
{
    // A single-line catch, a 138-column call, and a camelCase type name.
    private const string LayoutAndNamingSource =
        """
        namespace Sample
        {
            public class textColor
            {
                public void Run(string firstValue, string secondValue, string thirdValue, string fourthValue)
                {
                    string fifthValue = "5", sixthValue = "6", seventhValue = "7";
                    try
                    {
                        Failed(firstValue, secondValue, null, thirdValue, fourthValue, fifthValue, sixthValue, seventhValue, "a ninth argument.");
                    }
                    catch { }
                }

                private static void Failed(string a, string b, string c, string d, string e, string f, string g, string h, string i)
                {
                }
            }
        }

        """;

    // A public method name with an underscore (CA1707) next to a single-line catch (FDGA003).
    private const string UnderscoreSource =
        """
        namespace Sample
        {
            public class Worker
            {
                public void Do_Work()
                {
                    try
                    {
                        Do_Work();
                    }
                    catch { }
                }
            }
        }

        """;

    private const string FormattableSource =
        """
        namespace Sample
        {
            public class Worker
            {
                public void Run(string firstValue, string secondValue, string thirdValue, string fourthValue)
                {
                    string fifthValue = "5", sixthValue = "6", seventhValue = "7";
                    try
                    {
                        Failed(firstValue, secondValue, null, thirdValue, fourthValue, fifthValue, sixthValue, seventhValue, "a ninth argument.");
                    }
                    catch (System.Exception ex) { Log(ex); }

                    int first = 1; int second = 2;
                    if (first > second)
                        Log(null);
                }

                private static void Failed(string a, string b, string c, string d, string e, string f, string g, string h, string i)
                {
                }

                private static void Log(System.Exception ex)
                {
                }
            }
        }

        """;

    private readonly PackageFixture _fixture;

    public PackageTests(PackageFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Build_reports_layout_and_naming_warnings_for_application_profile()
    {
        using var workspace = new SampleWorkspace(_fixture, "layout");
        string project = workspace.AddProject("Sample", "application", LayoutAndNamingSource);

        DotnetResult build = workspace.Build(project);

        AssertSucceeded(build);
        Assert.Contains("warning FDGA003", build.Output);
        Assert.Contains("warning FDGA023", build.Output);
        Assert.Contains("warning IDE1006", build.Output);
        Assert.DoesNotContain("FDG0001", build.Output);
    }

    [Fact]
    public void Build_reports_mapped_ca_rule()
    {
        using var workspace = new SampleWorkspace(_fixture, "ca rule");
        string project = workspace.AddProject("Sample", "application", UnderscoreSource);

        DotnetResult build = workspace.Build(project);

        AssertSucceeded(build);
        Assert.Contains("warning CA1707", build.Output);
    }

    [Fact]
    public void Profile_wins_over_sdk_AnalysisLevel_globalconfigs()
    {
        // The SDK's analysislevel_*.globalconfig files use global_level -100, its per-category ones -99.
        using var workspace = new SampleWorkspace(_fixture, "analysis level");
        string all = workspace.AddProject(
            "AllMode",
            "test",
            UnderscoreSource,
            "<AnalysisLevel>latest-all</AnalysisLevel>");
        string noNaming = workspace.AddProject(
            "NoNaming",
            "application",
            UnderscoreSource,
            "<AnalysisLevel>latest</AnalysisLevel><AnalysisModeNaming>None</AnalysisModeNaming>");

        DotnetResult allBuild = workspace.Build(all);
        DotnetResult noNamingBuild = workspace.Build(noNaming);

        AssertSucceeded(allBuild);
        AssertSucceeded(noNamingBuild);
        Assert.DoesNotContain("MultipleGlobalAnalyzerKeys", allBuild.Output);
        Assert.DoesNotContain("CA1707", allBuild.Output);
        Assert.Contains("warning CA1707", noNamingBuild.Output);
    }

    [Fact]
    public void Tests_profile_does_not_report_api_design_rules()
    {
        using var workspace = new SampleWorkspace(_fixture, "test profile");
        string project = workspace.AddProject("Sample", "test", UnderscoreSource);

        DotnetResult build = workspace.Build(project);

        AssertSucceeded(build);
        Assert.DoesNotContain("CA1707", build.Output);
        // The profile is active: layout rules still apply to test code.
        Assert.Contains("warning FDGA003", build.Output);
    }

    [Fact]
    public void Missing_profile_emits_FDG0001_and_no_fdg_diagnostics()
    {
        using var workspace = new SampleWorkspace(_fixture, "no profile");
        string project = workspace.AddProject("Sample", profile: null, LayoutAndNamingSource);

        DotnetResult build = workspace.Build(project);

        AssertSucceeded(build);
        Assert.Contains(
            "warning FDG0001: FrameworkDesignGuidelinesProfile is not set; FDG rules are inactive. "
            + "Set it to library, application or test.",
            build.Output);
        Assert.DoesNotContain("FDGA", build.Output);
        Assert.DoesNotContain("IDE1006", build.Output);
    }

    [Fact]
    public void Invalid_profile_emits_FDG0002()
    {
        using var workspace = new SampleWorkspace(_fixture, "bad profile");
        string project = workspace.AddProject("Sample", "libary", LayoutAndNamingSource);

        DotnetResult build = workspace.Build(project);

        AssertSucceeded(build);
        Assert.Contains(
            "warning FDG0002: Unknown FrameworkDesignGuidelinesProfile 'libary'. Use library, application or test.",
            build.Output);
        Assert.DoesNotContain("FDGA", build.Output);
    }

    [Fact]
    public void User_editorconfig_overrides_package_severity()
    {
        using var workspace = new SampleWorkspace(
            _fixture,
            "editorconfig",
            "\n[*.cs]\ndotnet_diagnostic.IDE1006.severity = none\n");
        string project = workspace.AddProject("Sample", "application", LayoutAndNamingSource);

        DotnetResult build = workspace.Build(project);

        AssertSucceeded(build);
        Assert.DoesNotContain("IDE1006", build.Output);
        Assert.Contains("warning FDGA003", build.Output);
    }

    [Fact]
    public void Dotnet_format_fixes_package_codefixes()
    {
        using var workspace = new SampleWorkspace(_fixture, "format");
        string project = workspace.AddProject("Sample", "application", FormattableSource);
        AssertSucceeded(workspace.Dotnet("restore", project));

        DotnetResult format = workspace.Dotnet("format", project, "--no-restore");

        AssertSucceeded(format);
        string[] lines = workspace.Read(Path.Combine("Sample", "Sample.cs")).ReplaceLineEndings("\n").Split('\n');
        string[] trimmed = lines.Select(l => l.Trim()).ToArray();
        // FDGA003 code fix: the single-line catch is expanded.
        AssertSequence(trimmed, "catch (System.Exception ex)", "{", "Log(ex);", "}");
        // FDGA023 code fix: the long call is chopped before its first argument, one argument per line.
        AssertSequence(trimmed, "Failed(", "firstValue,", "secondValue,", "null,", "thirdValue,");
        AssertSequence(trimmed, "seventhValue,", "\"a ninth argument.\");");
        // csharp_preserve_single_line_statements = false: one statement per line.
        AssertSequence(trimmed, "int first = 1;", "int second = 2;");
        // IDE0011 (csharp_prefer_braces): the braceless if gets braces.
        AssertSequence(trimmed, "if (first > second)", "{", "Log(null);", "}");
        Assert.All(lines, l => Assert.True(l.Length <= 120, $"Line longer than 120 columns: {l}"));
    }

    [Fact]
    public void Marker_file_is_written_with_profile_and_lang_version()
    {
        using var workspace = new SampleWorkspace(_fixture, "marker");
        string project = workspace.AddProject("Sample", "application", UnderscoreSource);

        AssertSucceeded(workspace.Build(project));

        string markerPath = Path.Combine(workspace.DirectoryPath, "Sample", "obj", "fdg", "marker.json");
        using (JsonDocument marker = JsonDocument.Parse(File.ReadAllText(markerPath)))
        {
            JsonElement root = marker.RootElement;
            Assert.Equal(1, root.GetProperty("protocol").GetInt32());
            Assert.Equal(_fixture.Version, root.GetProperty("packageVersion").GetString());
            Assert.Equal("application", root.GetProperty("profile").GetString());
            Assert.Equal("7.3", root.GetProperty("langVersion").GetString());
            Assert.Equal("netstandard2.0", root.GetProperty("targetFramework").GetString());
            Assert.Equal(project, root.GetProperty("projectPath").GetString());
            string rulesPath = root.GetProperty("rulesPath").GetString()!;
            Assert.Equal(
                Path.Combine(_fixture.RestoredPackageDirectory, "data", "rules.json"),
                rulesPath,
                ignoreCase: OperatingSystem.IsWindows());
            Assert.True(File.Exists(rulesPath), rulesPath);
        }

        // An unchanged project does not rewrite the marker.
        DateTime written = File.GetLastWriteTimeUtc(markerPath);
        AssertSucceeded(workspace.Build(project));
        Assert.Equal(written, File.GetLastWriteTimeUtc(markerPath));
    }

    [Fact]
    public void Marker_files_are_written_per_target_framework()
    {
        using var workspace = new SampleWorkspace(_fixture, "multi");
        string project = workspace.AddProjectFromXml(
            "Sample",
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFrameworks>netstandard2.0;netstandard2.1</TargetFrameworks>
                <FrameworkDesignGuidelinesProfile>library</FrameworkDesignGuidelinesProfile>
              </PropertyGroup>
              <ItemGroup>
                {workspace.PackageReference}
              </ItemGroup>
            </Project>
            """,
            UnderscoreSource);

        AssertSucceeded(workspace.Build(project));

        string markerDirectory = Path.Combine(workspace.DirectoryPath, "Sample", "obj", "fdg");
        // Without LangVersion the marker carries each framework's default (MaxSupportedLangVersion).
        Assert.Equal(("netstandard2.0", "7.3"), ReadMarker(Path.Combine(markerDirectory, "marker.netstandard2.0.json")));
        Assert.Equal(("netstandard2.1", "8.0"), ReadMarker(Path.Combine(markerDirectory, "marker.netstandard2.1.json")));
        // marker.json is written by the first target framework only, so it is deterministic.
        Assert.Equal(("netstandard2.0", "7.3"), ReadMarker(Path.Combine(markerDirectory, "marker.json")));
    }

    [Fact]
    public void Sarif_is_written_when_FdgSarifDir_is_set()
    {
        using var workspace = new SampleWorkspace(_fixture, "sarif");
        string project = workspace.AddProject("Sample", "application", LayoutAndNamingSource);
        string sarifDirectory = Path.Combine(workspace.DirectoryPath, "sarif out");

        AssertSucceeded(workspace.Build(project, "-p:FdgSarifDir=" + sarifDirectory, "-p:FdgSarifProject=" + project));

        // dotnet-fdg passes a directory per project, so the log is named by target framework only.
        string sarifPath = Path.Combine(sarifDirectory, "netstandard2.0.sarif");
        Assert.True(File.Exists(sarifPath), sarifPath);
        using JsonDocument sarif = JsonDocument.Parse(File.ReadAllText(sarifPath));
        Assert.Equal("2.1.0", sarif.RootElement.GetProperty("version").GetString());
        string[] ruleIds = sarif.RootElement.GetProperty("runs")[0].GetProperty("results").EnumerateArray()
            .Select(r => r.GetProperty("ruleId").GetString()!)
            .ToArray();
        Assert.Contains("FDGA003", ruleIds);
        Assert.Contains("IDE1006", ruleIds);
    }

    [Fact]
    public void Sarif_is_written_when_FdgSarifProject_uses_forward_slashes()
    {
        using var workspace = new SampleWorkspace(_fixture, "sarif-slashes");
        string project = workspace.AddProject("Sample", "application", LayoutAndNamingSource);
        string sarifDirectory = Path.Combine(workspace.DirectoryPath, "sarif out");

        // Git Bash and other POSIX shells on Windows hand MSBuild paths such as D:/Repos/x/Sample.csproj.
        AssertSucceeded(workspace.Build(
            project,
            "-p:FdgSarifDir=" + sarifDirectory,
            "-p:FdgSarifProject=" + project.Replace('\\', '/')));

        string sarifPath = Path.Combine(sarifDirectory, "netstandard2.0.sarif");
        Assert.True(File.Exists(sarifPath), sarifPath);
    }

    [Fact]
    public void Package_does_not_flow_to_consumers()
    {
        using var workspace = new SampleWorkspace(_fixture, "flow");
        workspace.AddProject("Sample", "application", UnderscoreSource);
        string consumer = workspace.AddProjectFromXml(
            "Consumer",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>netstandard2.0</TargetFramework>
                <LangVersion>7.3</LangVersion>
                <FrameworkDesignGuidelinesProfile>application</FrameworkDesignGuidelinesProfile>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="..\Sample\Sample.csproj" />
              </ItemGroup>
            </Project>
            """,
            LayoutAndNamingSource.Replace("namespace Sample", "namespace Consumer"));

        DotnetResult build = workspace.Build(consumer);

        AssertSucceeded(build);
        string[] lines = build.Output.Split('\n');
        // The referenced project still gets its own diagnostics...
        Assert.Contains(lines, l => l.Contains("Sample.cs") && l.Contains("warning FDGA003"));
        // ...but nothing reaches the consumer: no analyzers, no globalconfig, no targets.
        Assert.DoesNotContain(lines, l => l.Contains("Consumer.cs") && (l.Contains("FDGA") || l.Contains("IDE1006")));
        Assert.DoesNotContain(lines, l => l.Contains("FDG0001"));
        Assert.False(Directory.Exists(Path.Combine(workspace.DirectoryPath, "Consumer", "obj", "fdg")));
    }

    private static (string TargetFramework, string LangVersion) ReadMarker(string path)
    {
        using JsonDocument marker = JsonDocument.Parse(File.ReadAllText(path));
        return (
            marker.RootElement.GetProperty("targetFramework").GetString()!,
            marker.RootElement.GetProperty("langVersion").GetString()!);
    }

    private static void AssertSucceeded(DotnetResult result)
    {
        Assert.True(result.ExitCode == 0, $"dotnet exited with {result.ExitCode}:\n{result.Output}");
    }

    private static void AssertSequence(string[] lines, params string[] expected)
    {
        for (int start = 0; start + expected.Length <= lines.Length; start++)
        {
            if (lines.Skip(start).Take(expected.Length).SequenceEqual(expected))
            {
                return;
            }
        }

        Assert.Fail($"Lines not found in order: {string.Join(" | ", expected)}\n{string.Join('\n', lines)}");
    }
}
