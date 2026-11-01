using Fdg.Cli.IO;

namespace Fdg.Cli.Tests;

public sealed class AtomicFileTests : IDisposable
{
    private readonly TempTree _tree = new();

    public void Dispose()
    {
        _tree.Dispose();
    }

    [Fact]
    public void Failed_write_leaves_the_previous_file()
    {
        string path = _tree.Write("state/ai-lines.json", "previous");

        Assert.Throws<InvalidOperationException>(() => AtomicFile.Write(path, stream =>
        {
            stream.Write("half"u8);
            throw new InvalidOperationException("The write failed halfway.");
        }));

        Assert.Equal("previous", File.ReadAllText(path));
        Assert.Equal([path], Directory.GetFiles(Path.GetDirectoryName(path)!));
    }

    [Fact]
    public void Write_replaces_an_existing_file_and_creates_a_missing_one()
    {
        string existing = _tree.Write("state/snapshot.json", "a much longer previous content");
        string missing = _tree.Path("state/中文 名.json");

        AtomicFile.WriteAllText(existing, "new");
        AtomicFile.WriteAllText(missing, "created");

        Assert.Equal("new", File.ReadAllText(existing));
        Assert.Equal("created", File.ReadAllText(missing));
        Assert.Equal(2, Directory.GetFiles(_tree.Path("state")).Length);
    }

    [Fact]
    public void Write_retries_while_the_target_is_briefly_in_use()
    {
        string path = _tree.Write("state/blocks.txt", "1");
        var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        // A thread of its own, so that a busy thread pool cannot delay the release past the retries.
        var release = new Thread(() =>
        {
            Thread.Sleep(30);
            locked.Dispose();
        });
        release.Start();

        AtomicFile.WriteAllText(path, "2");

        release.Join();
        Assert.Equal("2", File.ReadAllText(path));
        Assert.Equal([path], Directory.GetFiles(Path.GetDirectoryName(path)!));
    }

    [Fact]
    public void Copy_copies_the_content()
    {
        string source = _tree.Write("src/A.cs", "class A { }\n");
        string destination = _tree.Write("state/ai/a", "stale");

        AtomicFile.Copy(source, destination);

        Assert.Equal("class A { }\n", File.ReadAllText(destination));
    }

    [Fact]
    public void Write_through_a_symbolic_link_keeps_the_link()
    {
        string target = _tree.Write("real/A.cs", "old");
        string link = _tree.Path("linked/A.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        try
        {
            File.CreateSymbolicLink(link, target);
        }
        catch (IOException)
        {
            // Creating symbolic links needs developer mode or elevation on Windows.
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        AtomicFile.WriteAllText(link, "new");

        Assert.NotNull(new FileInfo(link).LinkTarget);
        Assert.Equal("new", File.ReadAllText(target));
    }
}
