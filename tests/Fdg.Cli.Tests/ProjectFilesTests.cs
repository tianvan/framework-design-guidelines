using Fdg.Cli.Projects;

namespace Fdg.Cli.Tests;

public sealed class ProjectFilesTests : IDisposable
{
    private readonly TempTree _tree = new();

    public void Dispose()
    {
        _tree.Dispose();
    }

    [Fact]
    public void Owning_projects_search_stops_at_the_boundary()
    {
        // Outside git, a .csproj above the user's home directory belongs to no session started below it.
        _tree.Write("Outer.csproj", TempTree.SdkProject);
        string home = _tree.Directory("home");
        string start = _tree.Directory("home/work/notes");

        Assert.Empty(ProjectFiles.FindOwningProjects(start, home));
        // Without the boundary, the search goes on up.
        Assert.Equal([_tree.Path("Outer.csproj")], ProjectFiles.FindOwningProjects(start, _tree.Path("elsewhere")));

        // The boundary itself is searched.
        string inHome = _tree.Write("home/Home.csproj", TempTree.SdkProject);
        Assert.Equal([inHome], ProjectFiles.FindOwningProjects(start, home + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void Owning_projects_search_still_stops_at_the_repository()
    {
        _tree.Write("Outer.csproj", TempTree.SdkProject);
        _tree.Directory("repo/.git");
        string start = _tree.Directory("repo/src");

        Assert.Empty(ProjectFiles.FindOwningProjects(start, _tree.Path("elsewhere")));
    }

    [Fact]
    public void Nearest_owning_projects_are_found_below_the_boundary()
    {
        string project = _tree.Write("home/work/App/App.csproj", TempTree.SdkProject);

        Assert.Equal([project], ProjectFiles.FindOwningProjects(_tree.Directory("home/work/App/Sub"), _tree.Path("home")));
    }
}
