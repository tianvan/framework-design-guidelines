using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Fdg.Generator.Rules;

/// <summary>Where a passage of book text that is not a guideline went: its rationale, background, or obsolete advice.</summary>
public enum NonGuidelineDisposition
{
    Rationale,
    Background,
    Obsolete,
}

public sealed record NonGuidelineEntry(string Section, NonGuidelineDisposition Disposition, string Note);

public sealed record ChapterMeta(string Token, string File, string Rules, string Title)
{
    /// <summary>The coverage-document entries for narrative text of this chapter; optional in <c>meta.yaml</c>.</summary>
    public IReadOnlyList<NonGuidelineEntry> NonGuideline { get; init; } = Array.Empty<NonGuidelineEntry>();
}

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
            dto.Chapters.Select(c => new ChapterMeta(c.Token, c.File, c.Rules, c.Title)
            {
                NonGuideline = c.NonGuideline.Select(e => ToEntry(c.Token, e)).ToList(),
            }).ToList());
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
            .Select(c => new RuleFile(c.Token, c.Title, c.File, LoadRules(Path.Combine(rulesDir, c.Rules)))
            {
                RulesFile = c.Rules,
                NonGuideline = c.NonGuideline,
            })
            .ToList();
    }

    private static NonGuidelineEntry ToEntry(string chapter, NonGuidelineDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Section) || string.IsNullOrWhiteSpace(dto.Note))
        {
            throw new FormatException($"meta.yaml: a nonGuideline entry of chapter {chapter} needs a section and a note.");
        }

        return new NonGuidelineEntry(
            dto.Section,
            RuleYaml.ParseEnum<NonGuidelineDisposition>(dto.Disposition, $"nonGuideline disposition of chapter {chapter}"),
            dto.Note);
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

        public List<NonGuidelineDto> NonGuideline { get; set; } = new();
    }

    private sealed class NonGuidelineDto
    {
        public string Section { get; set; } = string.Empty;

        public string Disposition { get; set; } = string.Empty;

        public string Note { get; set; } = string.Empty;
    }
}
