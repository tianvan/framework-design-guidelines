namespace Fdg.Cli.Tooling;

/// <summary>
/// Runs dotnet format until the files stop changing. A FixAll pass cannot fix nested occurrences, such as
/// single-line blocks inside each other, so one run is not always enough.
/// </summary>
public static class FormatPasses
{
    public const int MaxPasses = 3;

    /// <param name="hashSources">A hash over the files a pass may change; equal hashes end the loop.</param>
    /// <returns>The first failed run, or the last run when all succeeded.</returns>
    public static async Task<ToolResult> RunAsync(
        IFormatRunner formatter,
        string projectPath,
        IReadOnlyList<string>? includeFiles,
        IReadOnlyList<string> excludeDiagnostics,
        Func<string> hashSources,
        CancellationToken cancellationToken)
    {
        string before = hashSources();
        ToolResult result = new(0, string.Empty, false);
        for (int pass = 1; pass <= MaxPasses; pass++)
        {
            result = await formatter
                .FormatAsync(projectPath, includeFiles, excludeDiagnostics, cancellationToken)
                .ConfigureAwait(false);
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
}
