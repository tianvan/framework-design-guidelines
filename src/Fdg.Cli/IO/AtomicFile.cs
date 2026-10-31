using System.Text;

namespace Fdg.Cli.IO;

/// <summary>
/// Replaces files in one step, so that a reader, or the next run after a crash or a kill, finds either the old
/// content or the new one, never a partly written file.
/// </summary>
/// <remarks>
/// <para>
/// The new content goes to a uniquely named temporary file in the target's directory, is flushed to disk, and is
/// then moved over the target with <see cref="File.Move(string, string, bool)"/>, a rename within one volume. When
/// anything fails, the target is left as it was and the temporary file is deleted.
/// </para>
/// <para>
/// <see cref="File.Move(string, string, bool)"/> is used rather than <see cref="File.Replace(string, string, string?)"/>:
/// it works whether or not the target exists, and on the file systems where ReplaceFile fails (some network shares
/// and FAT volumes). The cost is that the target gets the temporary file's attributes, permissions and creation time,
/// which it inherits from the directory, instead of keeping its own; a read-only target is not replaced, the move
/// fails instead. Neither matters for the hook's state files, nor for the source files whose line endings are
/// restored right after dotnet format rewrote them.
/// </para>
/// <para>
/// The move is tried up to <see cref="MoveAttempts"/> times, <see cref="MoveRetryDelay"/> apart, since an editor, an
/// indexer or a virus scanner may hold the target open for a moment.
/// </para>
/// <para>
/// A symbolic link is followed: the file it finally points to is replaced, in that file's directory, and the link
/// stays a link. Without this, the move would replace the link itself with a regular file. A hard link is not kept:
/// the path gets a new file, and the other names keep the old content.
/// </para>
/// </remarks>
public static class AtomicFile
{
    private const int MoveAttempts = 3;

    private static readonly TimeSpan MoveRetryDelay = TimeSpan.FromMilliseconds(75);

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Writes to a temporary file next to <paramref name="path"/>, then moves it over <paramref name="path"/>.</summary>
    /// <param name="write">Writes the new content; the stream is flushed and closed afterwards.</param>
    public static void Write(string path, Action<Stream> write)
    {
        string target = ResolveLinks(Path.GetFullPath(path));
        string temporary = Path.Combine(
            Path.GetDirectoryName(target)!,
            "." + Path.GetFileName(target) + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                write(stream);
                stream.Flush(flushToDisk: true);
            }

            Move(temporary, target);
        }
        finally
        {
            // Gone after a successful move; after a failure, the target still has its old content.
            DeleteQuietly(temporary);
        }
    }

    /// <summary>Writes <paramref name="contents"/> as UTF-8 without a byte order mark, as File.WriteAllText does.</summary>
    public static void WriteAllText(string path, string contents) =>
        Write(path, stream => stream.Write(Utf8NoBom.GetBytes(contents)));

    public static void WriteAllBytes(string path, byte[] bytes) => Write(path, stream => stream.Write(bytes));

    /// <summary>Copies the content of <paramref name="source"/>; unlike File.Copy, not its attributes or times.</summary>
    public static void Copy(string source, string destination) =>
        Write(destination, stream =>
        {
            using FileStream input = File.OpenRead(source);
            input.CopyTo(stream);
        });

    private static void Move(string temporary, string target)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temporary, target, overwrite: true);
                return;
            }
            catch (Exception e) when (attempt < MoveAttempts && e is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(MoveRetryDelay);
            }
        }
    }

    /// <summary>Deletes the temporary file without hiding the exception that may be on its way out.</summary>
    private static void DeleteQuietly(string temporary)
    {
        try
        {
            File.Delete(temporary);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <returns>The file the symbolic links at <paramref name="path"/> finally point to, or <paramref name="path"/>.</returns>
    private static string ResolveLinks(string path)
    {
        if (new FileInfo(path).LinkTarget is null)
        {
            return path;
        }

        FileSystemInfo? target = File.ResolveLinkTarget(path, returnFinalTarget: true);
        return target is null ? path : Path.GetFullPath(target.FullName);
    }
}
