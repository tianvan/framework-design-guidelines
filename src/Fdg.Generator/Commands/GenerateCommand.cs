using System.Text.RegularExpressions;
using Fdg.Generator.Emit;
using YamlDotNet.Core;

namespace Fdg.Generator.Commands;

/// <summary>
/// <c>generate [--rules &lt;dir&gt;] [--out &lt;repository root&gt;] [--version &lt;v&gt;]</c>: writes the descriptors, globalconfigs,
/// rules manifest, skill references and docs from the rule files. Files that already match are left untouched.
/// </summary>
public static class GenerateCommand
{
    private static readonly string[] AllowedOptions = { "rules", "out", "version" };

    private static readonly Regex VersionElement = new(@"<Version>\s*([^<\s]+)\s*</Version>", RegexOptions.CultureInvariant);

    public static int Run(IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        if (!CommandOptions.TryParse(args, AllowedOptions, out CommandOptions options, out string message)
            || !RulesDirectory.TryResolve(options.Get("rules"), out string rulesDir, out message))
        {
            error.WriteLine(message);
            return 2;
        }

        string root = Path.GetFullPath(options.Get("out") ?? Path.GetDirectoryName(rulesDir)!);
        string? version = options.Get("version") ?? ReadVersion(root);
        if (version is null)
        {
            error.WriteLine($"Could not read <Version> from {Path.Combine(root, "Directory.Build.props")}; pass --version.");
            return 2;
        }

        try
        {
            IReadOnlyList<GeneratedFile> files = GeneratedFiles.Create(GeneratorInput.Load(rulesDir, version));
            int changed = GeneratedFiles.Write(root, files, out int removed);
            int written = changed - removed;
            output.WriteLine($"Generated {files.Count} files under {root}: {written} written, {removed} removed, {files.Count - written} unchanged.");
            return 0;
        }
        catch (GenerationException exception)
        {
            error.WriteLine($"Generation failed: {exception.Message}");
            return 1;
        }
        catch (Exception exception) when (exception is YamlException or FormatException or IOException)
        {
            error.WriteLine($"Could not read the rules: {exception.Message}");
            return 2;
        }
    }

    private static string? ReadVersion(string root)
    {
        string path = Path.Combine(root, "Directory.Build.props");
        if (!File.Exists(path))
        {
            return null;
        }

        Match match = VersionElement.Match(File.ReadAllText(path));
        return match.Success ? match.Groups[1].Value : null;
    }
}
