using System.Text.Json;
using Fdg.Cli.Hooks;
using Fdg.Cli.Tooling;

namespace Fdg.Cli.Tests.Hooks;

public sealed class SessionContextTests : IDisposable
{
    private const string Onboarded =
        "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
        "  <ItemGroup>\n" +
        "    <PackageReference Include=\"FrameworkDesignGuidelines.Analyzers\" PrivateAssets=\"all\" />\n" +
        "  </ItemGroup>\n" +
        "</Project>\n";

    private const string Plain = "<Project Sdk=\"Microsoft.NET.Sdk\" />\n";

    private const string Legacy =
        "<Project ToolsVersion=\"15.0\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">\n" +
        "  <ItemGroup>\n" +
        "    <PackageReference Include=\"FrameworkDesignGuidelines.Analyzers\" />\n" +
        "  </ItemGroup>\n" +
        "</Project>\n";

    private const string DisabledLine =
        "FDG setup issue: Not a git repository: end-of-turn checks are disabled. Run /fdg-setup to fix.";

    private readonly GitTestRepo _repo = new();

    private string _sdkVersion = "10.0.100";

    public void Dispose()
    {
        _repo.Dispose();
    }

    [Fact]
    public void No_package_reference_anywhere_returns_null()
    {
        _repo.Write("src/A/A.csproj", Plain);
        // Pins a version only; no project references the package.
        _repo.Write(
            "Directory.Packages.props",
            "<Project><ItemGroup><PackageVersion Include=\"FrameworkDesignGuidelines.Analyzers\" Version=\"0.1.0\" /></ItemGroup></Project>\n");

        Assert.Null(Build());
    }

    [Fact]
    public void Profiles_are_collapsed_by_directory()
    {
        Project("src/A/A.csproj", "application", "7.3", "net472", "netstandard2.0");
        Project("src/B/B.csproj", "application", "7.3", "netstandard2.0");
        Project("tests/T/T.csproj", "test", "7.3", "net472");

        Assert.Equal(
            "This repository follows Framework Design Guidelines (3rd ed.).\n" +
            "- Profiles: src/** = application, tests/** = test. C# 7.3; targets net472, netstandard2.0. Do not use newer syntax or APIs.\n" +
            "- Write in .NET BCL style: Allman braces, one statement per line, long parameter/argument lists chopped before the first item, one per line.\n" +
            "- Layout and machine-checkable rules are auto-fixed and checked at the end of each turn; only fix the FDG/CA/IDE issues reported to you.\n" +
            "- Before designing or changing types, members, or public APIs, use the fdg-write skill; use fdg-review to review an API.",
            Build());
    }

    [Fact]
    public void Mixed_profiles_in_same_directory_are_listed_per_project()
    {
        Project("src/A/A.csproj", "application");
        Project("src/B/B.csproj", "library");
        Project("src/C/C.csproj", "application");
        Project("tests/T/T.csproj", "test");
        Project("tests/U/U.csproj", "test");

        Assert.StartsWith(
            "- Profiles: src/A/** = application, src/B/** = library, src/C/** = application, tests/** = test. C# 7.3;",
            ProfilesLine(Build()));
    }

    [Fact]
    public void Projects_sharing_a_directory_are_listed_by_file_and_own_their_markers()
    {
        _repo.Write("src/A/A.csproj", Onboarded);
        // Both projects write to src/A/obj/fdg/; the marker there is the one A.Tests.csproj wrote last.
        Project("src/A/A.Tests.csproj", "test");
        Project("src/B/B.csproj", "test");

        string context = Build()!;

        Assert.StartsWith("- Profiles: src/A/A.Tests.csproj = test, src/B/** = test.", ProfilesLine(context));
        Assert.Equal(
            [
                "FDG setup issue: Never built with FrameworkDesignGuidelines.Analyzers (no obj/fdg/marker.json): " +
                "src/A/A.csproj. Run /fdg-setup to fix.",
            ],
            Issues(context));
    }

    [Fact]
    public void Projects_without_a_known_profile_keep_their_directory_from_collapsing()
    {
        Project("src/A/A.csproj", "application");
        Project("tests/T/T.csproj", "test");
        _repo.Write("tests/Plain/Plain.csproj", Plain);

        Assert.Equal(
            "- Profiles: src/** = application, tests/T/** = test. C# 7.3; targets netstandard2.0. Do not use newer syntax or APIs.",
            ProfilesLine(Build()));
    }

    [Fact]
    public void Lang_version_and_frameworks_come_from_markers()
    {
        Project("src/A/A.csproj", "library", "7.3", "net472", "netstandard2.0");
        Project("src/B/B.csproj", "library", "12", "net8.0");
        Project("src/C/C.csproj", "library", "latest", "net10.0");

        Assert.Equal(
            "- Profiles: src/** = library. C# 7.3, 12.0, latest; targets net10.0, net472, net8.0, netstandard2.0. " +
            "Do not use newer syntax or APIs.",
            ProfilesLine(Build()));
    }

    [Fact]
    public void Missing_profile_adds_setup_issue_line()
    {
        Project("src/A/A.csproj", string.Empty);
        Project("src/B/B.csproj", "application");

        string context = Build()!;

        Assert.Equal(
            "- Profiles: src/B/** = application. C# 7.3; targets netstandard2.0. Do not use newer syntax or APIs.",
            ProfilesLine(context));
        Assert.Equal(
            ["FDG setup issue: FrameworkDesignGuidelinesProfile is not library, application or test in src/A/A.csproj. Run /fdg-setup to fix."],
            Issues(context));
    }

    [Fact]
    public void Never_built_adds_setup_issue_line()
    {
        foreach (string name in (string[])["A", "B", "C", "D", "E"])
        {
            _repo.Write($"src/{name}/{name}.csproj", Onboarded);
        }

        string context = Build()!;

        Assert.Equal("- Profiles: none set yet.", ProfilesLine(context));
        Assert.Equal(
            [
                "FDG setup issue: Never built with FrameworkDesignGuidelines.Analyzers (no obj/fdg/marker.json): " +
                "src/A/A.csproj, src/B/B.csproj, src/C/C.csproj and 2 more. Run /fdg-setup to fix.",
            ],
            Issues(context));
    }

    [Fact]
    public void Protocol_mismatch_suggests_tool_update()
    {
        Project("src/A/A.csproj", "application", protocol: 2);

        Assert.Equal(
            [
                "FDG setup issue: FDG package protocol newer than this dotnet-fdg supports (1): src/A/A.csproj. " +
                "Update it with: dotnet tool update -g dotnet-fdg",
            ],
            Issues(Build()!));
    }

    [Fact]
    public void Legacy_projects_are_counted_once()
    {
        Project("src/A/A.csproj", "application");
        // One legacy project references the package itself, one through Directory.Build.props, one not at all.
        _repo.Write("legacy/One/One.csproj", Legacy);
        _repo.Write("legacy/Directory.Build.props", "<Project><ItemGroup><PackageReference Include=\"FrameworkDesignGuidelines.Analyzers\" /></ItemGroup></Project>\n");
        _repo.Write("legacy/Two/Two.csproj", "<Project ToolsVersion=\"15.0\" />\n");
        _repo.Write("old/Three.csproj", "<Project ToolsVersion=\"4.0\" />\n");
        _repo.Git("add", "legacy/One/One.csproj");

        string context = Build()!;

        Assert.Equal(
            ["FDG setup issue: 3 legacy (non-SDK) projects are not supported and are ignored. Run /fdg-setup to fix."],
            Issues(context));
        Assert.Equal(
            "- Profiles: src/** = application. C# 7.3; targets netstandard2.0. Do not use newer syntax or APIs.",
            ProfilesLine(context));

        // Tracked but deleted files are not projects.
        File.Delete(_repo.Path("legacy/One/One.csproj"));
        File.Delete(_repo.Path("legacy/Two/Two.csproj"));
        Assert.Equal(
            ["FDG setup issue: 1 legacy (non-SDK) project is not supported and is ignored. Run /fdg-setup to fix."],
            Issues(Build()!));
    }

    [Fact]
    public void Non_git_directory_adds_disabled_line()
    {
        using var tree = new TempTree();
        tree.Write("App/App.csproj", Onboarded);
        WriteMarker(tree.Path("App/App.csproj"), "application", "7.3", 1, ["netstandard2.0"]);

        string context = new SessionContext(DotnetRunner()).Build(tree.Root)!;

        Assert.Equal("- Profiles: App/** = application. C# 7.3; targets netstandard2.0. Do not use newer syntax or APIs.", ProfilesLine(context));
        Assert.Equal([DisabledLine], Issues(context));
    }

    [Fact]
    public void Non_git_directory_without_onboarded_projects_returns_null()
    {
        using var tree = new TempTree();
        tree.Write("App/App.csproj", Plain);

        Assert.Null(new SessionContext(DotnetRunner()).Build(tree.Root));
    }

    [Fact]
    public void Directory_Build_props_onboards_the_projects_below_it()
    {
        _repo.Write(
            "src/Directory.Build.props",
            "<Project><ItemGroup><PackageReference Include=\"FrameworkDesignGuidelines.Analyzers\" PrivateAssets=\"all\" /></ItemGroup></Project>\n");
        string project = _repo.Write("src/中文 A/A.csproj", Plain);
        WriteMarker(project, "library", "7.3", 1, ["netstandard2.0"]);
        // Not below the props file, not built: neither onboarded nor reported.
        _repo.Write("tools/X/X.csproj", Plain);

        string context = Build()!;

        Assert.Equal(
            "- Profiles: src/** = library. C# 7.3; targets netstandard2.0. Do not use newer syntax or APIs.",
            ProfilesLine(context));
        Assert.Empty(Issues(context));
    }

    [Fact]
    public void Old_or_missing_sdk_adds_setup_issue_line()
    {
        Project("src/A/A.csproj", "application");

        _sdkVersion = "7.0.410";
        Assert.Equal(
            ["FDG setup issue: .NET SDK 7.0.410 is older than 8.0.100. Run /fdg-setup to fix."],
            Issues(Build()!));
        _sdkVersion = "9.0.100-rc.1.24452.12";
        Assert.Empty(Issues(Build()!));

        var failing = new FakeProcessRunner().Respond(
            args => args is ["--version"],
            new ToolResult(1, "A compatible .NET SDK was not found.", false));
        Assert.Equal(
            ["FDG setup issue: dotnet --version failed here; FDG needs .NET SDK 8.0.100 or later. Run /fdg-setup to fix."],
            Issues(new SessionContext(failing).Build(_repo.Root)!));
    }

    [Fact]
    public void Output_stays_under_200_tokens_for_pilot_shaped_repo()
    {
        for (int i = 1; i <= 16; i++)
        {
            Project($"src/业务模块{i}/Company.Product.Module{i}.csproj", "application", "7.3", "net472", "netstandard2.0");
        }

        for (int i = 1; i <= 8; i++)
        {
            Project($"tests/业务模块{i}.Tests/Company.Product.Module{i}.Tests.csproj", "test", "7.3", "net472");
        }

        string context = Build()!;

        Assert.Empty(Issues(context));
        Assert.True(context.Length < 900, $"{context.Length} characters:\n{context}");
    }

    [Fact]
    public async Task Session_start_shows_setup_issues_to_the_user()
    {
        _repo.Write("src/A/A.csproj", Onboarded);
        var input = new HookInput(HostKind.Claude, "s", _repo.Path("src"), "SessionStart");

        using (JsonDocument output = JsonDocument.Parse(await CreateRunner().SessionStartAsync(input, CancellationToken.None)))
        {
            JsonElement root = output.RootElement;
            string issue =
                "FDG setup issue: Never built with FrameworkDesignGuidelines.Analyzers (no obj/fdg/marker.json): " +
                "src/A/A.csproj. Run /fdg-setup to fix.";
            Assert.Equal(issue, root.GetProperty("systemMessage").GetString());
            Assert.Equal("SessionStart", root.GetProperty("hookSpecificOutput").GetProperty("hookEventName").GetString());
            string context = root.GetProperty("hookSpecificOutput").GetProperty("additionalContext").GetString()!;
            Assert.StartsWith("This repository follows Framework Design Guidelines (3rd ed.).\n", context);
            Assert.EndsWith("\n" + issue, context);
        }

        WriteMarker(_repo.Path("src/A/A.csproj"), "application", "7.3", 1, ["netstandard2.0"]);
        using (JsonDocument output = JsonDocument.Parse(await CreateRunner().SessionStartAsync(input, CancellationToken.None)))
        {
            Assert.False(output.RootElement.TryGetProperty("systemMessage", out _));
            Assert.True(output.RootElement.TryGetProperty("hookSpecificOutput", out _));
        }
    }

    [Fact]
    public void Context_output_can_carry_a_system_message()
    {
        Assert.Equal(
            """{"systemMessage":"FDG setup issue: x","hookSpecificOutput":{"hookEventName":"SessionStart","additionalContext":"text\nFDG setup issue: x"}}""",
            HookOutput.Context("SessionStart", "text\nFDG setup issue: x", "FDG setup issue: x"));
        Assert.Equal(
            """{"hookSpecificOutput":{"hookEventName":"SessionStart","additionalContext":"text"}}""",
            HookOutput.Context("SessionStart", "text"));
    }

    private string? Build() => new SessionContext(DotnetRunner()).Build(_repo.Root);

    private FakeProcessRunner DotnetRunner() =>
        new FakeProcessRunner().Respond(args => args is ["--version"], new ToolResult(0, _sdkVersion + "\n", false));

    private HookRunner CreateRunner() =>
        new(new FakeHookProjects(), new FakeHookFormatter(), new FakeHookBuilder(), new SessionContext(DotnetRunner()), _ => null);

    /// <summary>An onboarded project that has been built: its csproj and the markers the package writes.</summary>
    private void Project(
        string relativePath,
        string profile,
        string langVersion = "7.3",
        params string[] frameworks) =>
        Project(relativePath, profile, langVersion, 1, frameworks);

    private void Project(string relativePath, string profile, int protocol) =>
        Project(relativePath, profile, "7.3", protocol, []);

    private void Project(string relativePath, string profile, string langVersion, int protocol, string[] frameworks)
    {
        string project = _repo.Write(relativePath, Onboarded);
        WriteMarker(project, profile, langVersion, protocol, frameworks.Length == 0 ? ["netstandard2.0"] : frameworks);
    }

    /// <summary>
    /// Like the package: one framework writes <c>marker.json</c>; several write <c>marker.&lt;tfm&gt;.json</c> each, and
    /// the first also <c>marker.json</c>.
    /// </summary>
    private static void WriteMarker(string projectPath, string profile, string langVersion, int protocol, string[] frameworks)
    {
        string directory = Path.Combine(Path.GetDirectoryName(projectPath)!, "obj", "fdg");
        Directory.CreateDirectory(directory);
        string Marker(string framework) => JsonSerializer.Serialize(new
        {
            protocol,
            packageVersion = "0.1.0",
            profile,
            langVersion,
            targetFramework = framework,
            rulesPath = "C:\\nuget\\frameworkdesignguidelines.analyzers\\0.1.0\\data\\rules.json",
            projectPath,
        });
        File.WriteAllText(Path.Combine(directory, "marker.json"), Marker(frameworks[0]));
        if (frameworks.Length > 1)
        {
            foreach (string framework in frameworks)
            {
                File.WriteAllText(Path.Combine(directory, $"marker.{framework}.json"), Marker(framework));
            }
        }
    }

    private static string ProfilesLine(string? context)
    {
        Assert.NotNull(context);
        return Assert.Single(context.Split('\n'), l => l.StartsWith("- Profiles:", StringComparison.Ordinal));
    }

    private static string[] Issues(string context) =>
        context.Split('\n').Where(l => l.StartsWith("FDG setup issue:", StringComparison.Ordinal)).ToArray();
}
