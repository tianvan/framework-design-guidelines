using Fdg.Generator.Plugin;
using Fdg.Generator.Rules;
using Fdg.Generator.Sdk;
using YamlDotNet.Core;

namespace Fdg.Generator.Commands;

/// <summary>
/// <c>check [--book &lt;OEBPS dir&gt;]</c>: validates every rule file against the schema and the mapping
/// conventions, and every referenced CA/IDE diagnostic against the installed SDK. With <c>--book</c> it also
/// runs <c>verify-source</c>. It also checks that the plugin manifests carry the version of
/// <c>Directory.Build.props</c> in the parent of the rules directory.
/// </summary>
public static class CheckCommand
{
    private static readonly string[] AllowedOptions = { "book", "rules", "sdk" };

    public static int Run(IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        return ToolErrors.Run("check", error, () => Execute(args, output, error));
    }

    private static int Execute(IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        if (!CommandOptions.TryParse(args, AllowedOptions, out CommandOptions options, out string message)
            || !RulesDirectory.TryResolve(options.Get("rules"), out string rulesDir, out message))
        {
            error.WriteLine(message);
            return 2;
        }

        IReadOnlyDictionary<string, SdkDiagnostic> catalog;
        try
        {
            string sdkDir = options.Get("sdk") ?? SdkLocator.FindSdkDirectory(Path.GetDirectoryName(rulesDir)!);
            catalog = SdkDiagnosticCatalog.Load(sdkDir);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            error.WriteLine($"Could not load the SDK's analyzers: {exception.Message}");
            return 2;
        }

        IReadOnlyList<CheckError> errors = Check(rulesDir, catalog);
        foreach (CheckError checkError in errors)
        {
            error.WriteLine(checkError);
        }

        int exitCode = errors.Count > 0 ? 1 : 0;
        if (errors.Count == 0)
        {
            output.WriteLine($"Rule mappings are valid against {catalog.Count} SDK diagnostics.");
        }
        else
        {
            error.WriteLine($"{errors.Count} problem(s) found.");
        }

        string? bookDir = options.Get("book");
        if (bookDir is not null)
        {
            exitCode = Math.Max(exitCode, VerifySourceCommand.Run(new[] { "--book", bookDir, "--rules", rulesDir }, output, error));
        }

        return exitCode;
    }

    /// <summary>
    /// Collects every problem in the rules directory, and in the plugin manifest versions of the repository that
    /// contains it; files that fail the schema skip the mapping checks.
    /// </summary>
    public static IReadOnlyList<CheckError> Check(string rulesDir, IReadOnlyDictionary<string, SdkDiagnostic> catalog)
    {
        var errors = new List<CheckError>();
        SchemaValidator schema = SchemaValidator.Load(Path.Combine(rulesDir, SchemaValidator.FileName));

        IReadOnlyDictionary<string, IReadOnlyList<string>> profiles;
        try
        {
            profiles = ProfileFile.Load(rulesDir);
            errors.AddRange(ConfigValidator.ValidateProfiles(profiles));
        }
        catch (Exception exception) when (exception is YamlException or IOException)
        {
            errors.Add(new CheckError(ProfileFile.FileName, CheckError.NoRule, exception.Message));
            profiles = new Dictionary<string, IReadOnlyList<string>>();
        }

        IReadOnlyList<ChapterMeta> metaChapters;
        try
        {
            metaChapters = RuleLoader.LoadMeta(rulesDir).Chapters;
        }
        catch (Exception exception) when (exception is YamlException or FormatException)
        {
            errors.Add(new CheckError("meta.yaml", CheckError.NoRule, exception.Message));
            metaChapters = Array.Empty<ChapterMeta>();
        }

        var chapters = new List<ChapterRules>();
        foreach (ChapterMeta chapter in metaChapters)
        {
            string path = Path.Combine(rulesDir, chapter.Rules);
            if (!File.Exists(path))
            {
                errors.Add(new CheckError(chapter.Rules, CheckError.NoRule, $"{path} does not exist."));
                continue;
            }

            IReadOnlyList<CheckError> schemaErrors = schema.Validate(chapter.Rules, File.ReadAllText(path));
            if (schemaErrors.Count > 0)
            {
                errors.AddRange(schemaErrors);
                continue;
            }

            try
            {
                chapters.Add(new ChapterRules(chapter, RuleLoader.LoadRules(path)));
            }
            catch (Exception exception) when (exception is YamlException or FormatException)
            {
                errors.Add(new CheckError(chapter.Rules, CheckError.NoRule, exception.Message));
            }
        }

        errors.AddRange(MappingValidator.Validate(chapters, profiles, catalog));
        errors.AddRange(ManifestVersionValidator.Validate(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(rulesDir)))!));
        return errors;
    }
}
