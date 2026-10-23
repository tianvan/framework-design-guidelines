using Fdg.Cli.Hooks;

namespace Fdg.Cli.Tests.Hooks;

public sealed class SnapshotTests : IDisposable
{
    private const string Project = "<Project Sdk=\"Microsoft.NET.Sdk\" />\n";

    private GitTestRepo _repo = new();

    public void Dispose()
    {
        _repo.Dispose();
    }

    [Fact]
    public async Task Turn_start_copies_dirty_in_scope_files_to_baseline()
    {
        _repo.Write("App/App.csproj", Project);
        _repo.Write("App/A.cs", "class A { }\n");
        _repo.Write("App/Clean.cs", "class Clean { }\n");
        _repo.CommitAll();
        _repo.Write("App/A.cs", "class A { int x; }\n");
        _repo.Write("App/New.cs", "class New { }\n");
        _repo.Write("App/obj/Generated.cs", "class Generated { }\n");
        _repo.Write("Loose/NoProject.cs", "class Loose { }\n");
        (GitRepository git, StateStore state) = await OpenAsync();

        TurnSnapshot snapshot = await Snapshot.TakeAsync(git, state, CancellationToken.None);

        Assert.Equal(["App/A.cs", "App/New.cs"], snapshot.DirtyFiles.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("class A { int x; }\n", File.ReadAllText(state.BaselinePath("App/A.cs")));
        Assert.Equal("class New { }\n", File.ReadAllText(state.BaselinePath("App/New.cs")));
        Assert.False(File.Exists(state.BaselinePath("App/Clean.cs")));
        Assert.StartsWith(Path.Combine(git.GitDirectory, "fdg", "s1") + Path.DirectorySeparatorChar, state.BaselinePath("App/A.cs"));
        Assert.Equal(snapshot.Turn, state.ReadSnapshot()!.Turn);
    }

    [Fact]
    public async Task Clean_file_baseline_is_head_content_via_cat_file_filters()
    {
        ResetRepo(autoCrlf: true);
        _repo.Write("App/App.csproj", Project);
        _repo.Write("App/A.cs", "class A\r\n{\r\n}\r\n");
        _repo.CommitAll();
        (GitRepository git, StateStore state) = await OpenAsync();
        TurnSnapshot snapshot = await Snapshot.TakeAsync(git, state, CancellationToken.None);
        _repo.Write("App/A.cs", "class A\r\n{\r\n    int x;\r\n}\r\n");

        string baseline = await Snapshot.WriteBaselineAsync(git, state, snapshot, "App/A.cs", CancellationToken.None);

        // The blob is stored with LF; --filters applies autocrlf like a checkout does.
        Assert.Equal("class A\r\n{\r\n}\r\n", File.ReadAllText(baseline));
    }

    [Fact]
    public async Task Crlf_working_tree_with_autocrlf_yields_only_real_changed_lines()
    {
        ResetRepo(autoCrlf: true);
        _repo.Write("App/App.csproj", Project);
        string source = string.Join("\r\n", Enumerable.Range(1, 10).Select(i => $"// line {i}")) + "\r\n";
        _repo.Write("App/A.cs", source);
        _repo.CommitAll();
        (GitRepository git, StateStore state) = await OpenAsync();
        TurnSnapshot snapshot = await Snapshot.TakeAsync(git, state, CancellationToken.None);
        string current = _repo.Write("App/A.cs", source.Replace("// line 4\r\n", "// line four\r\n"));

        Assert.Equal(["App/A.cs"], await Snapshot.FindChangedFilesAsync(git, snapshot, CancellationToken.None));
        string baseline = await Snapshot.WriteBaselineAsync(git, state, snapshot, "App/A.cs", CancellationToken.None);
        IReadOnlySet<int> lines = await ChangedLines.ComputeAsync(git, baseline, current, CancellationToken.None);

        Assert.Equal([4], lines.Order());
    }

    [Fact]
    public async Task Repository_without_commits_uses_empty_baseline()
    {
        _repo.Write("App/App.csproj", Project);
        (GitRepository git, StateStore state) = await OpenAsync();
        TurnSnapshot snapshot = await Snapshot.TakeAsync(git, state, CancellationToken.None);
        string current = _repo.Write("App/A.cs", "class A\n{\n}\n");

        Assert.Null(snapshot.Head);
        Assert.Equal(["App/A.cs"], await Snapshot.FindChangedFilesAsync(git, snapshot, CancellationToken.None));
        string baseline = await Snapshot.WriteBaselineAsync(git, state, snapshot, "App/A.cs", CancellationToken.None);
        Assert.Equal(0, new FileInfo(baseline).Length);
        Assert.Equal([1, 2, 3], (await ChangedLines.ComputeAsync(git, baseline, current, CancellationToken.None)).Order());
    }

    [Fact]
    public async Task File_created_via_shell_is_detected_as_changed()
    {
        _repo.Write("App/App.csproj", Project);
        _repo.Write("App/A.cs", "class A { }\n");
        _repo.Write("App/Dirty.cs", "class Dirty { }\n");
        _repo.CommitAll();
        _repo.Write("App/Dirty.cs", "class Dirty { int before; }\n");
        _repo.Write("App/DirtyUntouched.cs", "class DirtyUntouched { }\n");
        (GitRepository git, StateStore state) = await OpenAsync();
        TurnSnapshot snapshot = await Snapshot.TakeAsync(git, state, CancellationToken.None);

        // What a shell command such as `echo ... > file` leaves behind: no edit tool involved.
        _repo.Write("App/Sub Dir/Shell.cs", "class Shell { }\n");
        _repo.Write("App/Dirty.cs", "class Dirty { int before; int after; }\n");

        IReadOnlyList<string> changed = await Snapshot.FindChangedFilesAsync(git, snapshot, CancellationToken.None);

        Assert.Equal(["App/Dirty.cs", "App/Sub Dir/Shell.cs"], changed);
    }

    [Fact]
    public async Task Changed_lines_ignore_whitespace_only_reformatting()
    {
        (GitRepository git, _) = await OpenAsync();
        string before = _repo.Write("before.cs", "class A\n{\nint x;\n  int y;\n}\n");
        string after = _repo.Write("after.cs", "class A\n{\n    int x;\n    int z;\n}\n");

        IReadOnlySet<int> lines = await ChangedLines.ComputeAsync(git, before, after, CancellationToken.None);

        Assert.Equal([4], lines.Order());
    }

    [Fact]
    public async Task Changed_lines_ignore_user_inter_hunk_context()
    {
        _repo.Git("config", "diff.interHunkContext", "10");
        (GitRepository git, _) = await OpenAsync();
        string before = _repo.Write("before.cs", "a\nb\nc\nd\ne\nf\n");
        string after = _repo.Write("after.cs", "a\nB\nc\nd\ne\nF\n");

        IReadOnlySet<int> lines = await ChangedLines.ComputeAsync(git, before, after, CancellationToken.None);

        Assert.Equal([2, 6], lines.Order());
    }

    [Theory]
    [InlineData("@@ -3 +3 @@\n-a\n+b\n", "3")]
    [InlineData("@@ -3,0 +4,2 @@\n+a\n+b\n@@ -10,2 +12,0 @@\n-c\n-d\n", "4,5")]
    [InlineData("@@ -0,0 +1,3 @@ class A\n+a\n+b\n+c\n", "1,2,3")]
    [InlineData("", "")]
    public void Hunk_headers_give_new_side_lines(string diff, string expected)
    {
        IReadOnlySet<int> lines = ChangedLines.Lines(ChangedLines.ParseHunks(diff));

        Assert.Equal(expected, string.Join(',', lines.Order()));
    }

    [Fact]
    public async Task Git_status_paths_with_spaces_and_cjk_are_parsed_from_z_output()
    {
        _repo.Write("中文 目录/Tracked File.cs", "class T { }\n");
        _repo.CommitAll();
        _repo.Write("中文 目录/Tracked File.cs", "class T { int x; }\n");
        _repo.Write("中文 目录/新 文件.cs", "class N { }\n");
        _repo.Write("#hash [x].cs", "class H { }\n");
        (GitRepository git, _) = await OpenAsync();

        IReadOnlyList<GitStatusEntry> entries = await git.StatusAsync(CancellationToken.None);

        Assert.Equal(
            [" M 中文 目录/Tracked File.cs", "?? #hash [x].cs", "?? 中文 目录/新 文件.cs"],
            entries.Select(e => e.Status + " " + e.Path).Order(StringComparer.Ordinal));
        // A rename entry is followed by its source path.
        Assert.Equal(
            ["R  new name.cs", "?? 中文.cs"],
            GitStatusEntry.Parse("R  new name.cs\0old name.cs\0?? 中文.cs\0").Select(e => e.Status + " " + e.Path));
    }

    [Fact]
    public async Task First_commit_made_during_the_turn_counts()
    {
        _repo.Write("App/App.csproj", Project);
        (GitRepository git, StateStore state) = await OpenAsync();
        TurnSnapshot snapshot = await Snapshot.TakeAsync(git, state, CancellationToken.None);
        _repo.Write("App/A.cs", "class A { }\n");
        _repo.CommitAll("the AI's first commit");

        TurnChanges changes = await Snapshot.FindChangesAsync(git, snapshot, CancellationToken.None);

        Assert.Equal(["App/A.cs"], changes.Changed);
        Assert.Empty(changes.Notes);
    }

    [Fact]
    public async Task Commits_made_during_the_turn_count_and_those_from_before_it_say_so()
    {
        _repo.Write("App/App.csproj", Project);
        _repo.Write("App/A.cs", "class A { }\n");
        _repo.Write("App/B.cs", "class B { }\n");
        _repo.CommitAll();
        _repo.Git("switch", "-q", "-c", "side");
        _repo.Write("App/B.cs", "class B { int old; }\n");
        _repo.CommitAll("on another branch, before the turn", DateTimeOffset.UtcNow.AddDays(-1));
        _repo.Git("switch", "-q", "main");
        (GitRepository git, StateStore state) = await OpenAsync();
        TurnSnapshot snapshot = await Snapshot.TakeAsync(git, state, CancellationToken.None);
        _repo.Git("merge", "-q", "--no-ff", "--no-edit", "side");
        _repo.Write("App/A.cs", "class A { int x; }\n");
        _repo.CommitAll("the AI commits");

        TurnChanges changes = await Snapshot.FindChangesAsync(git, snapshot, CancellationToken.None);

        // The merge commit is new, but a merge brings in no file of its own.
        Assert.Equal(["App/A.cs"], changes.Changed);
        Assert.Equal(["FDG: HEAD moved during the turn by commits not made in it; their files were not checked."], changes.Notes);
    }

    [Fact]
    public async Task Head_that_is_no_longer_a_descendant_of_the_turns_head_says_so()
    {
        _repo.Write("App/App.csproj", Project);
        _repo.Write("App/A.cs", "class A { }\n");
        _repo.CommitAll();
        _repo.Git("branch", "side");
        _repo.Write("App/A.cs", "class A { int main; }\n");
        _repo.CommitAll();
        (GitRepository git, StateStore state) = await OpenAsync();
        TurnSnapshot snapshot = await Snapshot.TakeAsync(git, state, CancellationToken.None);
        _repo.Git("switch", "-q", "side");

        TurnChanges changes = await Snapshot.FindChangesAsync(git, snapshot, CancellationToken.None);

        Assert.Empty(changes.Changed);
        Assert.Equal(["FDG: HEAD moved during the turn by commits not made in it; their files were not checked."], changes.Notes);
    }

    [Fact]
    public async Task Turn_head_that_cannot_be_read_gives_one_note()
    {
        _repo.Write("App/App.csproj", Project);
        _repo.Write("App/A.cs", "class A { }\n");
        _repo.CommitAll();
        (GitRepository git, _) = await OpenAsync();
        var snapshot = new TurnSnapshot(1, new string('1', 40), new Dictionary<string, string>(), DateTimeOffset.UtcNow);

        TurnChanges changes = await Snapshot.FindChangesAsync(git, snapshot, CancellationToken.None);

        Assert.Empty(changes.Changed);
        Assert.StartsWith("FDG: cannot list the commits made during the turn (", Assert.Single(changes.Notes));
        Assert.EndsWith("); their files were not checked.", changes.Notes[0]);
    }

    [Fact]
    public async Task Snapshot_without_a_start_time_started_when_its_file_was_written()
    {
        (GitRepository git, StateStore state) = await OpenAsync();
        Directory.CreateDirectory(state.Directory);
        string path = Path.Combine(state.Directory, "snapshot.json");
        File.WriteAllText(path, """{ "turn": 3, "head": null, "dirtyFiles": {} }""");
        var written = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, written);

        TurnSnapshot snapshot = state.ReadSnapshot()!;

        Assert.Equal(new DateTimeOffset(written), snapshot.Started);
    }

    private void ResetRepo(bool autoCrlf)
    {
        _repo.Dispose();
        _repo = new GitTestRepo(autoCrlf);
    }

    private async Task<(GitRepository Git, StateStore State)> OpenAsync()
    {
        GitRepository git = (await GitRepository.OpenAsync(_repo.Root, CancellationToken.None))!;
        Assert.NotNull(git);
        return (git, StateStore.For(git, "s1"));
    }
}
