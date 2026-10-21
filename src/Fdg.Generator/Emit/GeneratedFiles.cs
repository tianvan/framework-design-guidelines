using System.Text;
using Fdg.Generator.Rules;

namespace Fdg.Generator.Emit;

/// <summary>Collects every generated file and writes them under a repository root.</summary>
public static class GeneratedFiles
{
    private const string RuleDocsDirectory = "docs/rules";

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public static IReadOnlyList<GeneratedFile> Create(GeneratorInput input)
    {
        var files = new List<GeneratedFile>
        {
            new("src/FrameworkDesignGuidelines.Analyzers/Generated/FdgDescriptors.g.cs", DescriptorEmitter.Emit(input)),
            new("src/FrameworkDesignGuidelines.Package/data/rules.json", RulesJsonEmitter.Emit(input)),
            new("docs/coverage.md", CoverageEmitter.Emit(input)),
        };

        foreach (string profile in ProfileFile.Names)
        {
            files.Add(new GeneratedFile(
                $"src/FrameworkDesignGuidelines.Package/build/globalconfigs/fdg.{profile}.globalconfig",
                GlobalConfigEmitter.Emit(input, profile)));
        }

        files.AddRange(SkillReferenceEmitter.Emit(input));
        files.AddRange(RuleDocEmitter.Emit(input));
        return files.OrderBy(f => f.Path, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Writes the files whose content differs from disk, and removes rule pages of rules that no longer exist.
    /// Returns how many files were written or removed; an unchanged file keeps its timestamp.
    /// </summary>
    public static int Write(string root, IReadOnlyList<GeneratedFile> files)
    {
        int changes = 0;
        foreach (GeneratedFile file in files)
        {
            string path = Path.Combine(root, file.Path);
            byte[] content = Utf8.GetBytes(file.Content);
            if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(content))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, content);
            changes++;
        }

        string docsDir = Path.Combine(root, RuleDocsDirectory);
        if (Directory.Exists(docsDir))
        {
            var expected = files.Select(f => Path.GetFullPath(Path.Combine(root, f.Path))).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (string stale in Directory.GetFiles(docsDir, "FDG*.md").Where(f => !expected.Contains(Path.GetFullPath(f))))
            {
                File.Delete(stale);
                changes++;
            }
        }

        return changes;
    }
}
