using System.Text.RegularExpressions;
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

/// <summary>One pair of skill reference files of a chapter: <c>references/&lt;File&gt;.md</c> holds the rules of the listed sections.</summary>
public sealed record ReferenceSplit(string File, IReadOnlyList<string> Sections);

public sealed record ChapterMeta(string Token, string File, string Rules, string Title)
{
    /// <summary>
    /// How the chapter's skill references are split by section prefix; optional in <c>meta.yaml</c>. Empty: one pair of
    /// files for the whole chapter.
    /// </summary>
    public IReadOnlyList<ReferenceSplit> References { get; init; } = Array.Empty<ReferenceSplit>();

    /// <summary>The coverage-document entries for narrative text of this chapter; optional in <c>meta.yaml</c>.</summary>
    public IReadOnlyList<NonGuidelineEntry> NonGuideline { get; init; } = Array.Empty<NonGuidelineEntry>();
}

public sealed record MetaFile(string RepositoryUrl, IReadOnlyList<ChapterMeta> Chapters);

/// <summary>Loads <c>meta.yaml</c> and the rule files it lists.</summary>
public static class RuleLoader
{
    private const string MetaFileName = "meta.yaml";

    private static readonly Regex SplitStem = new("^[a-z0-9-]+$");

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
                References = (c.References ?? new()).Select(r => ToSplit(c.Token, r)).ToList(),
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
                References = c.References,
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

    private static ReferenceSplit ToSplit(string chapter, ReferenceSplitDto? dto)
    {
        if (dto is null || string.IsNullOrWhiteSpace(dto.File) || dto.Sections is null || dto.Sections.Count == 0
            || dto.Sections.Any(string.IsNullOrWhiteSpace))
        {
            throw new FormatException($"meta.yaml: a references entry of chapter {chapter} needs a file and at least one section.");
        }

        if (!SplitStem.IsMatch(dto.File))
        {
            throw new FormatException(
                $"meta.yaml: reference file '{dto.File}' of chapter {chapter} must contain only lowercase letters, digits and '-'.");
        }

        return new ReferenceSplit(dto.File, dto.Sections.Select(s => s!).ToList());
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

        public List<ReferenceSplitDto?>? References { get; set; }
    }

    private sealed class ReferenceSplitDto
    {
        public string? File { get; set; }

        public List<string?>? Sections { get; set; }
    }

    private sealed class NonGuidelineDto
    {
        public string Section { get; set; } = string.Empty;

        public string Disposition { get; set; } = string.Empty;

        public string Note { get; set; } = string.Empty;
    }
}
