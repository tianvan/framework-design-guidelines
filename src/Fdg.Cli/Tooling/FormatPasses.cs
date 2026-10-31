namespace Fdg.Cli.Tooling;

/// <summary>
/// Runs dotnet format until the files stop changing. A FixAll pass cannot fix nested occurrences, such as
/// single-line blocks inside each other, so one run is not always enough. After each run, every source file
/// gets back the line ending it had before the first run (see <see cref="LineEndings"/>), also when the run fails
/// or throws.
/// </summary>
public static class FormatPasses
{
    public const int MaxPasses = 3;

    /// <param name="sourceFiles">The full paths of the files a pass may change.</param>
    /// <param name="hashSources">A hash over the files a pass may change; equal hashes end the loop.</param>
    /// <returns>The first failed run, or the last run when all succeeded.</returns>
    public static async Task<ToolResult> RunAsync(
        IFormatRunner formatter,
        string projectPath,
        IReadOnlyList<string>? includeFiles,
        IReadOnlyList<string> excludeFiles,
        IReadOnlyList<string> diagnostics,
        IReadOnlyList<string> sourceFiles,
        Func<string> hashSources,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, string> endings = LineEndings.Capture(sourceFiles);
        string before = hashSources();
        ToolResult result = new(0, string.Empty, false);
        for (int pass = 1; pass <= MaxPasses; pass++)
        {
            try
            {
                result = await formatter
                    .FormatAsync(projectPath, includeFiles, excludeFiles, diagnostics, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Best effort, so that a failure to restore cannot hide the formatter's exception.
                TryRestore(endings);
                throw;
            }

            // After every run, also a failed one: dotnet format may have written some files already. One run covers
            // all batches of a long include list, so each file is restored once per pass.
            LineEndings.Restore(endings);

            if (!result.Succeeded)
            {
                return result;
            }

            string after = hashSources();
            if (after == before)
            {
                break;
            }

            before = after;
        }

        return result;
    }

    /// <summary>Restores what it can while an exception passes.</summary>
    private static void TryRestore(IReadOnlyDictionary<string, string> endings)
    {
        try
        {
            LineEndings.Restore(endings);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
