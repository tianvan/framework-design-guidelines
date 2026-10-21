using Fdg.Generator.Rules;

namespace Fdg.Generator.Emit;

/// <summary>Everything the emitters read: the rules, <c>meta.yaml</c>, <c>profiles.yaml</c>, and the package version.</summary>
public sealed record GeneratorInput(
    MetaFile Meta,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Profiles,
    IReadOnlyList<RuleFile> Files,
    string Version)
{
    public IEnumerable<Rule> Rules => Files.SelectMany(f => f.Rules);

    public static GeneratorInput Load(string rulesDir, string version)
    {
        return new GeneratorInput(
            RuleLoader.LoadMeta(rulesDir),
            ProfileFile.Load(rulesDir),
            RuleLoader.LoadAll(rulesDir),
            version);
    }
}
