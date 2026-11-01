using Fdg.Cli.Projects;

namespace Fdg.Cli.Tests;

public sealed class ProjectReferencesTests : IDisposable
{
    private readonly TempTree _tree = new();

    public void Dispose()
    {
        _tree.Dispose();
    }

    [Fact]
    public void Closure_holds_the_referenced_projects_transitively_whatever_their_condition()
    {
        string app = _tree.Write("App/App.csproj", Project("..\\Lib\\Lib.csproj", "../Other/Other.csproj", condition: "'$(X)' == 'y'"));
        string lib = _tree.Write("Lib/Lib.csproj", Project("$(MSBuildThisFileDirectory)../Core/Core.csproj"));
        string other = _tree.Write("Other/Other.csproj", Project("../Core/Core.csproj;../App/App.csproj"));
        string core = _tree.Write("Core/Core.csproj", TempTree.SdkProject);

        IReadOnlySet<string>? closure = ProjectReferences.Closure(app);

        Assert.NotNull(closure);
        Assert.Equal(new[] { lib, other, core }.Order(), closure.Order());
    }

    [Fact]
    public void Closure_is_null_for_an_msbuild_property_in_a_reference()
    {
        string app = _tree.Write("App/App.csproj", Project("..\\Lib\\Lib.csproj"));
        _tree.Write("Lib/Lib.csproj", Project("$(SrcDir)Core/Core.csproj"));
        _tree.Write("Core/Core.csproj", TempTree.SdkProject);

        Assert.Null(ProjectReferences.Closure(app));
    }

    [Fact]
    public void Closure_is_null_for_a_reference_that_cannot_be_read()
    {
        string app = _tree.Write("App/App.csproj", Project("../Missing/Missing.csproj"));

        Assert.Null(ProjectReferences.Closure(app));
    }

    [Fact]
    public void Closure_is_null_when_a_directory_build_file_adds_references()
    {
        _tree.Write("Directory.Build.targets", Project("$(MSBuildThisFileDirectory)Core/Core.csproj"));
        string app = _tree.Write("App/App.csproj", TempTree.SdkProject);

        Assert.Null(ProjectReferences.Closure(app));
    }

    private static string Project(string include, string? conditional = null, string? condition = null) =>
        "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
        "  <ItemGroup>\n" +
        $"    <ProjectReference Include=\"{include}\" />\n" +
        "  </ItemGroup>\n" +
        (conditional is null
            ? string.Empty
            : $"  <ItemGroup Condition=\"{condition}\">\n    <ProjectReference Include=\"{conditional}\" />\n  </ItemGroup>\n") +
        "</Project>\n";
}
