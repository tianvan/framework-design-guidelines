namespace Fdg.Cli.Tests;

/// <summary>A temporary directory tree whose path contains a space and CJK characters.</summary>
public sealed class TempTree : IDisposable
{
    public const string SdkProject = "<Project Sdk=\"Microsoft.NET.Sdk\" />\n";

    public TempTree()
    {
        Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fdg-cli 测试 " + Guid.NewGuid().ToString("N")[..8]);
        System.IO.Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string Path(string relativePath) => System.IO.Path.GetFullPath(System.IO.Path.Combine(Root, relativePath));

    /// <summary>Writes a file and returns its full path.</summary>
    public string Write(string relativePath, string content = "")
    {
        string path = Path(relativePath);
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public string Directory(string relativePath) => System.IO.Directory.CreateDirectory(Path(relativePath)).FullName;

    public void Dispose()
    {
        try
        {
            // git makes object files read-only, which Directory.Delete refuses to remove.
            foreach (string file in System.IO.Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            System.IO.Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
