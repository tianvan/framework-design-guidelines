namespace Fdg.IntegrationTests;

internal static class TempDirectory
{
    /// <summary>Deletes a directory tree, retrying while a just-exited build process still holds a handle.</summary>
    public static void Delete(string path)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }

                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(500);
            }
        }
    }
}
