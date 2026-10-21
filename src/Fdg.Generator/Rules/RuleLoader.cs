using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Fdg.Generator.Rules;

public sealed record ChapterMeta(string Token, string File, string Rules, string Title);

public sealed record MetaFile(string RepositoryUrl, IReadOnlyList<ChapterMeta> Chapters);

/// <summary>Loads <c>meta.yaml</c> and the rule files it lists.</summary>
public static class RuleLoader
{
    private const string MetaFileName = "meta.yaml";

    private static readonly IDeserializer MetaDeserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    public static MetaFile LoadMeta(string rulesDir)
    {
        string path = Path.Combine(rulesDir, MetaFileName);
        MetaDto dto = MetaDeserializer.Deserialize<MetaDto>(File.ReadAllText(path))
            ?? throw new FormatException($"{path} is empty.");

        return new MetaFile(
            dto.RepositoryUrl,
            dto.Chapters.Select(c => new ChapterMeta(c.Token, c.File, c.Rules, c.Title)).ToList());
    }

    public static IReadOnlyList<Rule> LoadRules(string path)
    {
        return RuleYaml.Deserialize(File.ReadAllText(path));
    }

    /// <summary>Loads every chapter listed in <c>meta.yaml</c>, in the listed order.</summary>
    public static IReadOnlyList<RuleFile> LoadAll(string rulesDir)
    {
        MetaFile meta = LoadMeta(rulesDir);
        return meta.Chapters
            .Select(c => new RuleFile(c.Token, c.Title, c.File, LoadRules(Path.Combine(rulesDir, c.Rules))))
            .ToList();
    }

    private sealed class MetaDto
    {
        public string RepositoryUrl { get; set; } = string.Empty;

        public List<ChapterDto> Chapters { get; set; } = new();
    }

    private sealed class ChapterDto
    {
        public string Token { get; set; } = string.Empty;

        public string File { get; set; } = string.Empty;

        public string Rules { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;
    }
}
