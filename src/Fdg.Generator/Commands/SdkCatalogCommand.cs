using Fdg.Generator.Sdk;

namespace Fdg.Generator.Commands;

/// <summary>
/// <c>sdk-catalog [--sdk &lt;dir&gt;]</c> (development aid, not in the usage text): prints every C# CA/IDE diagnostic
/// of the installed SDK as tab-separated id, default severity, code fix availability and title.
/// </summary>
public static class SdkCatalogCommand
{
    private static readonly string[] AllowedOptions = { "sdk" };

    public static int Run(IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        if (!CommandOptions.TryParse(args, AllowedOptions, out CommandOptions options, out string message))
        {
            error.WriteLine(message);
            return 2;
        }

        IReadOnlyDictionary<string, SdkDiagnostic> catalog;
        try
        {
            catalog = SdkDiagnosticCatalog.Load(options.Get("sdk") ?? SdkLocator.FindSdkDirectory(Directory.GetCurrentDirectory()));
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            error.WriteLine($"Could not load the SDK's analyzers: {exception.Message}");
            return 2;
        }

        foreach (SdkDiagnostic diagnostic in catalog.Values)
        {
            string fix = diagnostic.HasCodeFix ? "fix" : "-";
            output.WriteLine($"{diagnostic.Id}\t{diagnostic.DefaultSeverity}\t{fix}\t{diagnostic.Title}");
        }

        return 0;
    }
}
