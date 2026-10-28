using System.Text;
using Fdg.Generator.Rules;

namespace Fdg.Generator.Emit;

/// <summary>Collects every generated file and writes them under a repository root.</summary>
public static class GeneratedFiles
{
    private const string RuleDocsDirectory = "docs/rules";
    private const string ReferencesDirectory = "plugins/framework-design-guidelines/skills/fdg-write/references";

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
    /// Writes the files whose content differs from disk, and removes the rule pages and skill reference files that are
    /// no longer generated. Returns how many files were written or removed; an unchanged file keeps its timestamp.
    /// </summary>
    public static int Write(string root, IReadOnlyList<GeneratedFile> files) => Write(root, files, out _);

    /// <summary>
    /// Like <see cref="Write(string, IReadOnlyList{GeneratedFile})"/>, and reports through <paramref name="removed"/>
    /// how many of the returned changes were removals of stale files.
    /// </summary>
    public static int Write(string root, IReadOnlyList<GeneratedFile> files, out int removed)
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

        var expected = files
            .Select(f => Path.GetFullPath(Path.Combine(root, f.Path)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        removed = RemoveStale(root, RuleDocsDirectory, "FDG*.md", recurse: false, expected)
            + RemoveStale(root, ReferencesDirectory, "*.md", recurse: true, expected);
        return changes + removed;
    }

    /// <summary>
    /// Deletes the files under <paramref name="directory"/> (relative to <paramref name="root"/>) that match
    /// <paramref name="pattern"/> and are not in <paramref name="expected"/>. Returns how many it deleted.
    /// </summary>
    private static int RemoveStale(
        string root,
        string directory,
        string pattern,
        bool recurse,
        HashSet<string> expected)
    {
        string path = Path.Combine(root, directory);
        if (!Directory.Exists(path))
        {
            return 0;
        }

        int removed = 0;
        foreach (string file in Directory.GetFiles(
            path,
            pattern,
            new EnumerationOptions { RecurseSubdirectories = recurse, AttributesToSkip = FileAttributes.ReparsePoint }))
        {
            if (!expected.Contains(Path.GetFullPath(file)))
            {
                File.Delete(file);
                removed++;
            }
        }

        return removed;
    }
}
