using Fdg.Cli.Hooks;

namespace Fdg.Cli.Tests.Hooks;

public sealed class OnboardingTests : IDisposable
{
    private const string Plain = "<Project Sdk=\"Microsoft.NET.Sdk\" />\n";

    private const string Reference =
        "<Project><ItemGroup><PackageReference Include=\"FrameworkDesignGuidelines.Analyzers\" PrivateAssets=\"all\" /></ItemGroup></Project>\n";

    private readonly TempTree _tree = new();

    public void Dispose()
    {
        _tree.Dispose();
    }

    [Fact]
    public void Package_reference_in_the_project_counts()
    {
        string project = _tree.Write("src/A/A.csproj", Reference.Replace("<Project>", "<Project Sdk=\"Microsoft.NET.Sdk\">"));

        Assert.True(IsOnboarded(project));
    }

    [Fact]
    public void Package_id_in_a_comment_or_a_version_pin_does_not_count()
    {
        _tree.Write(
            "Directory.Build.props",
            "<Project>\n" +
            "  <!-- <PackageReference Include=\"FrameworkDesignGuidelines.Analyzers\" /> -->\n" +
            "  <ItemGroup><PackageVersion Include=\"FrameworkDesignGuidelines.Analyzers\" Version=\"0.1.0\" /></ItemGroup>\n" +
            "</Project>\n");
        string project = _tree.Write(
            "src/A/A.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <!-- FrameworkDesignGuidelines.Analyzers is added later -->\n" +
            "  <PropertyGroup><Note>FrameworkDesignGuidelines.Analyzers</Note></PropertyGroup>\n" +
            "</Project>\n");

        Assert.False(IsOnboarded(project));
    }

    [Fact]
    public void Child_Directory_Build_props_without_an_import_of_its_parent_hides_the_parent()
    {
        _tree.Write("Directory.Build.props", Reference);
        _tree.Write("tools/Directory.Build.props", "<Project><PropertyGroup><X>1</X></PropertyGroup></Project>\n");
        string tool = _tree.Write("tools/T/T.csproj", Plain);
        string app = _tree.Write("src/A/A.csproj", Plain);

        Assert.False(IsOnboarded(tool));
        Assert.True(IsOnboarded(app));
    }

    [Theory]
    [InlineData("$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))")]
    [InlineData("$([MSBuild]::GetPathOfFileAbove(Directory.Build.props, $(MSBuildThisFileDirectory)..))")]
    [InlineData("..\\Directory.Build.props")]
    [InlineData("$(MSBuildThisFileDirectory)../Directory.Build.props")]
    [InlineData("$([MSBuild]::GetPathOfFileAbove('$(MSBuildThisFile)', '$(MSBuildThisFileDirectory)../'))")]
    public void Child_Directory_Build_props_that_imports_its_parent_follows_it(string import)
    {
        _tree.Write("Directory.Build.props", Reference);
        _tree.Write("tools/Directory.Build.props", $"<Project><Import Project=\"{import}\" /></Project>\n");
        string tool = _tree.Write("tools/T/T.csproj", Plain);

        Assert.True(IsOnboarded(tool));
    }

    [Fact]
    public void Import_through_a_property_of_the_same_file_follows_it_as_fdg_setup_writes_it()
    {
        _tree.Write("Directory.Build.props", Reference);
        // The import fdg-setup's template writes into a new Directory.Build.props.
        _tree.Write(
            "tools/Directory.Build.props",
            """
            <Project>
              <PropertyGroup>
                <ParentDirectoryBuildProps>$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))</ParentDirectoryBuildProps>
              </PropertyGroup>
              <Import Project="$(ParentDirectoryBuildProps)" Condition="'$(ParentDirectoryBuildProps)' != ''" />

              <PropertyGroup>
                <FrameworkDesignGuidelinesProfile>application</FrameworkDesignGuidelinesProfile>
              </PropertyGroup>
            </Project>
            """);
        string tool = _tree.Write("tools/T/T.csproj", Plain);

        Assert.True(IsOnboarded(tool));
    }

    [Fact]
    public void Property_is_substituted_one_level_only()
    {
        _tree.Write("Directory.Build.props", Reference);
        _tree.Write(
            "tools/Directory.Build.props",
            "<Project>\n" +
            "  <PropertyGroup><Inner>../Directory.Build.props</Inner><Outer>$(Inner)</Outer></PropertyGroup>\n" +
            "  <Import Project=\"$(Outer)\" />\n" +
            "</Project>\n");
        string tool = _tree.Write("tools/T/T.csproj", Plain);

        Assert.False(IsOnboarded(tool));
    }

    [Fact]
    public void Imports_that_loop_end()
    {
        _tree.Write("Directory.Build.props", "<Project><Import Project=\"src/Directory.Build.props\" /></Project>\n");
        _tree.Write("src/Directory.Build.props", "<Project><Import Project=\"../Directory.Build.props\" /></Project>\n");
        string project = _tree.Write("src/A/A.csproj", Plain);

        Assert.False(IsOnboarded(project));
    }

    [Fact]
    public void Global_package_reference_in_Directory_Packages_props_counts()
    {
        _tree.Write(
            "Directory.Packages.props",
            "<Project><ItemGroup><GlobalPackageReference Include=\"FrameworkDesignGuidelines.Analyzers\" Version=\"0.1.0\" /></ItemGroup></Project>\n");
        string project = _tree.Write("src/A/A.csproj", Plain);

        Assert.True(IsOnboarded(project));
    }

    [Fact]
    public void Package_reference_in_Directory_Build_targets_or_an_update_counts()
    {
        _tree.Write("src/Directory.Build.targets", Reference);
        string fromTargets = _tree.Write("src/A/A.csproj", Plain);
        string updated = _tree.Write(
            "lib/B/B.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><PackageReference Update=\"FrameworkDesignGuidelines.Analyzers\" /></ItemGroup></Project>\n");

        Assert.True(IsOnboarded(fromTargets));
        Assert.True(IsOnboarded(updated));
    }

    [Fact]
    public void Legacy_project_in_the_msbuild_namespace_counts()
    {
        string project = _tree.Write(
            "legacy/L.csproj",
            "<Project ToolsVersion=\"15.0\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">\n" +
            "  <ItemGroup><PackageReference Include=\"frameworkdesignguidelines.analyzers\" /></ItemGroup>\n" +
            "</Project>\n");

        Assert.True(IsOnboarded(project));
    }

    [Fact]
    public void Project_assets_decide_when_present()
    {
        string restoredWithout = _tree.Write("src/A/A.csproj", Reference);
        _tree.Write("src/A/obj/project.assets.json", "{\"libraries\":{\"Other/1.0.0\":{}}}");
        string restoredWith = _tree.Write("src/B/B.csproj", Reference);
        _tree.Write("src/B/obj/project.assets.json", "{\"libraries\":{\"FrameworkDesignGuidelines.Analyzers/0.1.0\":{}}}");

        Assert.False(IsOnboarded(restoredWithout));
        Assert.True(IsOnboarded(restoredWith));
    }

    [Fact]
    public void Unreadable_project_files_do_not_count()
    {
        string project = _tree.Write("src/A/A.csproj", "<Project><ItemGroup><PackageReference Include=\"FrameworkDesignGuidelines.Analyzers\"");

        Assert.False(IsOnboarded(project));
    }

    [Fact]
    public async Task Repository_is_onboarded_when_any_project_is()
    {
        using var repo = new GitTestRepo();
        repo.Write("src/A/A.csproj", Plain);
        repo.Write("README.md", "FrameworkDesignGuidelines.Analyzers");
        GitRepository git = (await GitRepository.OpenAsync(repo.Root, CancellationToken.None))!;

        Assert.False(await Onboarding.IsRepositoryOnboardedAsync(git, CancellationToken.None));

        repo.Write("src/Directory.Build.props", Reference);
        Assert.True(await Onboarding.IsRepositoryOnboardedAsync(git, CancellationToken.None));
    }

    private bool IsOnboarded(string projectPath) => new Onboarding(_tree.Root).IsOnboarded(projectPath);
}
