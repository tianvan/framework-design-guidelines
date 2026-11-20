namespace Fdg.Cli.IO;

internal static class FileLease
{
    public static async Task<FileStream> AcquireAsync(string path, CancellationToken cancellationToken)
    {
        DateTime expires = DateTime.UtcNow.AddMinutes(10);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                // Leave the (empty) lock file in place: deleting it would open a race between waiters.
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (DateTime.UtcNow < expires)
            {
                await Task.Delay(150, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
