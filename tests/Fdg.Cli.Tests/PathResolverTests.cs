using Fdg.Cli.Projects;

namespace Fdg.Cli.Tests;

public sealed class PathResolverTests : IDisposable
{
    private readonly TempTree _tree = new();

    public void Dispose()
    {
        _tree.Dispose();
    }

    [Fact]
    public void Directory_resolves_all_projects_skipping_bin_obj_and_hidden()
    {
        string a = _tree.Write("src/A/A.csproj", TempTree.SdkProject);
        string b = _tree.Write("src/Nested/B/B.csproj", TempTree.SdkProject);
        _tree.Write("src/A/bin/Debug/Copy.csproj", TempTree.SdkProject);
        _tree.Write("src/A/obj/Generated.csproj", TempTree.SdkProject);
        _tree.Write(".hidden/H.csproj", TempTree.SdkProject);
        _tree.Write("src/.vs/V.csproj", TempTree.SdkProject);

        IReadOnlyList<FixTarget> targets = PathResolver.Resolve([], _tree.Root);

        Assert.Equal([a, b], targets.Select(t => t.ProjectPath));
        Assert.All(targets, t => Assert.Null(t.IncludeFiles));
    }

    [Fact]
    public void Directory_inside_project_becomes_include_filter()
    {
        string project = _tree.Write("A/A.csproj", TempTree.SdkProject);
        string directory = _tree.Directory("A/中文 目录");
        _tree.Write("A/中文 目录/X.cs");

        FixTarget target = Assert.Single(PathResolver.Resolve(["A/中文 目录"], _tree.Root));

        Assert.Equal(project, target.ProjectPath);
        Assert.Equal([directory], target.IncludeFiles);
    }

    [Fact]
    public void Cs_file_resolves_to_nearest_project_with_include()
    {
        _tree.Write("Outer.csproj", TempTree.SdkProject);
        string inner = _tree.Write("src/Inner/Inner.csproj", TempTree.SdkProject);
        string file = _tree.Write("src/Inner/Deep/Code.cs");

        FixTarget target = Assert.Single(PathResolver.Resolve([file], _tree.Root));

        Assert.Equal(inner, target.ProjectPath);
        Assert.Equal([file], target.IncludeFiles);
    }

    [Fact]
    public void Csproj_path_resolves_to_whole_project()
    {
        string project = _tree.Write("A/A.csproj", TempTree.SdkProject);

        FixTarget target = Assert.Single(PathResolver.Resolve(["A/A.csproj"], _tree.Root));

        Assert.Equal(project, target.ProjectPath);
        Assert.Null(target.IncludeFiles);
    }

    [Fact]
    public void Multiple_paths_merge_per_project()
    {
        string a = _tree.Write("A/A.csproj", TempTree.SdkProject);
        string b = _tree.Write("B/B.csproj", TempTree.SdkProject);
        string one = _tree.Write("A/One.cs");
        string two = _tree.Write("A/Sub/Two.cs");
        string bFile = _tree.Write("B/Three.cs");

        IReadOnlyList<FixTarget> targets = PathResolver.Resolve(["A/One.cs", two, "A/One.cs", bFile, "B"], _tree.Root);

        Assert.Equal(2, targets.Count);
        Assert.Equal(a, targets[0].ProjectPath);
        Assert.Equal([one, two], targets[0].IncludeFiles);
        // A whole-project path wins over file includes for the same project.
        Assert.Equal(b, targets[1].ProjectPath);
        Assert.Null(targets[1].IncludeFiles);
    }

    [Fact]
    public void Cs_file_outside_any_project_resolves_to_nothing()
    {
        string file = _tree.Write("loose/Code.cs");

        Assert.Empty(PathResolver.Resolve([file], _tree.Root));
    }

    [Fact]
    public void Missing_path_is_an_error()
    {
        var exception = Assert.Throws<CommandException>(() => PathResolver.Resolve(["nope"], _tree.Root));

        Assert.Contains("nope", exception.Message);
    }
}
