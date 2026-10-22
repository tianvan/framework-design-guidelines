using Xunit.Abstractions;

namespace Fdg.IntegrationTests;

/// <summary>Runs the installed <c>dotnet fdg</c> tool against throwaway solutions.</summary>
[Trait("Category", "Integration")]
public sealed class FixEndToEndTests : IClassFixture<PackageFixture>
{
    // One line with two statements, a single-line catch, a 138-column call, a braceless if, and a camelCase type
    // name whose fix (IDE1006, a rename) is breaking.
    private const string ViolatingSource =
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
    private readonly ITestOutputHelper _log;

    public FixEndToEndTests(PackageFixture fixture, ITestOutputHelper log)
    {
        _fixture = fixture;
        _log = log;
    }

    [Fact]
    public void Fix_solution_fixes_profiled_project_and_skips_the_other()
    {
        using var workspace = new SampleWorkspace(_fixture, "fix");
        workspace.AddProject("Fixed", "application", ViolatingSource);
        workspace.Write(Path.Combine("Fixed", "中文 目录", "Nested.cs"), ViolatingSource.Replace("textColor", "nestedColor"));
        AddPlainProject(workspace);
        byte[] plainBefore = File.ReadAllBytes(Path.Combine(workspace.DirectoryPath, "Plain", "Plain.cs"));
        CreateSolution(workspace, "Sample.sln", "sln");

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        DotnetResult fix = workspace.Fdg("fix", "Sample.sln");
        _log.WriteLine($"dotnet fdg fix Sample.sln ({stopwatch.Elapsed.TotalSeconds:F1} s, exit {fix.ExitCode}):\n{fix.Output}");

        Assert.True(fix.ExitCode == 0, $"dotnet fdg exited with {fix.ExitCode}:\n{fix.Output}");
        string[] output = fix.Output.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("fixed Fixed/Fixed.csproj", output);
        Assert.Contains("skipped Plain/Plain.csproj: FrameworkDesignGuidelines.Analyzers is not referenced", output);
        AssertFixed(workspace.Read(Path.Combine("Fixed", "Fixed.cs")), "textColor");
        AssertFixed(workspace.Read(Path.Combine("Fixed", "中文 目录", "Nested.cs")), "nestedColor");
        Assert.Equal(plainBefore, File.ReadAllBytes(Path.Combine(workspace.DirectoryPath, "Plain", "Plain.cs")));

        // Nothing fixable is left: the layout rules no longer fire.
        DotnetResult build = workspace.Build(Path.Combine(workspace.DirectoryPath, "Fixed", "Fixed.csproj"));
        Assert.True(build.ExitCode == 0, build.Output);
        Assert.DoesNotContain("FDGA003", build.Output);
        Assert.DoesNotContain("FDGA023", build.Output);
        Assert.DoesNotContain("IDE0011", build.Output);
        // The breaking rename was not applied, so it is still reported.
        Assert.Contains("warning IDE1006", build.Output);
    }

    [Fact]
    public void Fix_file_under_cjk_directory_changes_only_that_file()
    {
        using var workspace = new SampleWorkspace(_fixture, "fix file");
        workspace.AddProject("Fixed", "application", ViolatingSource);
        string nested = Path.Combine("Fixed", "中文 目录", "Nested.cs");
        workspace.Write(nested, ViolatingSource.Replace("textColor", "nestedColor"));
        byte[] otherBefore = File.ReadAllBytes(Path.Combine(workspace.DirectoryPath, "Fixed", "Fixed.cs"));

        DotnetResult fix = workspace.Fdg("fix", nested);

        Assert.True(fix.ExitCode == 0, $"dotnet fdg exited with {fix.ExitCode}:\n{fix.Output}");
        Assert.Contains("fixed Fixed/Fixed.csproj", fix.Output);
        AssertFixed(workspace.Read(nested), "nestedColor");
        Assert.Equal(otherBefore, File.ReadAllBytes(Path.Combine(workspace.DirectoryPath, "Fixed", "Fixed.cs")));
    }

    [Theory]
    [InlineData("sln")]
    [InlineData("slnx")]
    public void Solution_and_slnx_list_projects(string format)
    {
        using var workspace = new SampleWorkspace(_fixture, "list " + format);
        workspace.AddProject("Fixed", profile: null, "namespace Sample { }\n");
        AddPlainProject(workspace);
        CreateSolution(workspace, "Sample." + format, format);

        DotnetResult fix = workspace.Fdg("fix", "Sample." + format);

        Assert.True(fix.ExitCode == 0, $"dotnet fdg exited with {fix.ExitCode}:\n{fix.Output}");
        string[] output = fix.Output.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(
            [
                "skipped Fixed/Fixed.csproj: FrameworkDesignGuidelinesProfile is not set",
                "skipped Plain/Plain.csproj: FrameworkDesignGuidelines.Analyzers is not referenced",
            ],
            output.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Version_prints_the_package_version()
    {
        using var workspace = new SampleWorkspace(_fixture, "version");

        DotnetResult version = workspace.Fdg("--version");

        Assert.Equal(0, version.ExitCode);
        Assert.Equal(_fixture.Version, version.Output.Trim());
    }

    private static void AddPlainProject(SampleWorkspace workspace)
    {
        workspace.AddProjectFromXml(
            "Plain",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>netstandard2.0</TargetFramework>
                <LangVersion>7.3</LangVersion>
              </PropertyGroup>
            </Project>
            """,
            ViolatingSource.Replace("namespace Sample", "namespace Plain"));
    }

    private static void CreateSolution(SampleWorkspace workspace, string fileName, string format)
    {
        DotnetResult create = workspace.Dotnet(
            "new",
            "sln",
            "--name",
            Path.GetFileNameWithoutExtension(fileName),
            "--format",
            format);
        Assert.True(create.ExitCode == 0, create.Output);
        string[] projects = Directory.GetFiles(workspace.DirectoryPath, "*.csproj", SearchOption.AllDirectories);
        DotnetResult add = workspace.Dotnet(["sln", fileName, "add", .. projects]);
        Assert.True(add.ExitCode == 0, add.Output);
    }

    private static void AssertFixed(string source, string typeName)
    {
        string[] lines = source.ReplaceLineEndings("\n").Split('\n');
        string[] trimmed = lines.Select(l => l.Trim()).ToArray();
        // The breaking rename (IDE1006) is excluded.
        Assert.Contains($"public class {typeName}", trimmed);
        // FDGA003: the single-line catch is expanded.
        AssertSequence(trimmed, "catch (System.Exception ex)", "{", "Log(ex);", "}");
        // FDGA023: the long call is chopped, one argument per line.
        AssertSequence(trimmed, "Failed(", "firstValue,", "secondValue,", "null,", "thirdValue,");
        // One statement per line.
        AssertSequence(trimmed, "int first = 1;", "int second = 2;");
        // IDE0011: the braceless if gets braces.
        AssertSequence(trimmed, "if (first > second)", "{", "Log(null);", "}");
        Assert.All(lines, l => Assert.True(l.Length <= 120, $"Line longer than 120 columns: {l}"));
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
