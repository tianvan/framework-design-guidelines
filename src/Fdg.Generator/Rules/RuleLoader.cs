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
    /// files for the whole chapter. A section written after <c>=</c>, such as <c>=9.12</c>, matches only that section,
    /// not its subsections; every rule must match exactly one split.
    /// </summary>
    public IReadOnlyList<ReferenceSplit> References { get; init; } = Array.Empty<ReferenceSplit>();

    /// <summary>
    /// Whether the chapter's rules apply to test code (<c>testProfile: on</c>, the default). When off, the rules'
    /// <c>severity.test</c> must be <c>none</c> unless the notes explain the exception.
    /// </summary>
    public bool TestProfileOn { get; init; } = true;

    /// <summary>The coverage-document entries for narrative text of this chapter; optional in <c>meta.yaml</c>.</summary>
    public IReadOnlyList<NonGuidelineEntry> NonGuideline { get; init; } = Array.Empty<NonGuidelineEntry>();
}

/// <summary>
/// A part of the book outside the rule-bearing chapters, such as the preface or appendix B: the xhtml file it comes
/// from, its title, and where its text went.
/// </summary>
public sealed record OtherContent(string Source, string Title, IReadOnlyList<NonGuidelineEntry> Entries);

public sealed record MetaFile(string RepositoryUrl, IReadOnlyList<ChapterMeta> Chapters)
{
    /// <summary>The coverage-document entries for the parts of the book without rules; optional.</summary>
    public IReadOnlyList<OtherContent> OtherContent { get; init; } = Array.Empty<OtherContent>();
}

/// <summary>Loads <c>meta.yaml</c> and the rule files it lists.</summary>
public static class RuleLoader
{
    private const string MetaFileName = "meta.yaml";

    private static readonly Regex SplitStem = new("^[a-z0-9-]+$");

    /// <summary>
    /// A references section: a section number (a chapter number or an appendix letter, then dotted numbers),
    /// optionally after <c>=</c> to leave out its subsections.
    /// </summary>
    private static readonly Regex SplitSection = new(@"\A=?([0-9]+|[A-Z])(\.[0-9]+)*\z");

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
                NonGuideline = c.NonGuideline.Select(e => ToEntry("nonGuideline", $"chapter {c.Token}", e)).ToList(),
                References = (c.References ?? new()).Select(r => ToSplit(c.Token, r)).ToList(),
                TestProfileOn = ToTestProfile(c.Token, c.TestProfile),
            }).ToList())
        {
            OtherContent = (dto.OtherContent ?? new()).Select(ToOtherContent).ToList(),
        };
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

    /// <summary>
    /// Converts one entry of the <paramref name="list"/> (<c>nonGuideline</c> or <c>otherContent</c>) of
    /// <paramref name="owner"/>, a chapter or a source file, which name the entry in errors.
    /// </summary>
    private static NonGuidelineEntry ToEntry(string list, string owner, NonGuidelineDto? dto)
    {
        if (dto is null || string.IsNullOrWhiteSpace(dto.Section) || string.IsNullOrWhiteSpace(dto.Note))
        {
            throw new FormatException($"meta.yaml: each {list} entry of {owner} needs a section and a note.");
        }

        return new NonGuidelineEntry(
            dto.Section,
            RuleYaml.ParseEnum<NonGuidelineDisposition>(dto.Disposition, $"{list} disposition of {owner}"),
            dto.Note);
    }

    private static OtherContent ToOtherContent(OtherContentDto? dto)
    {
        if (dto is null || string.IsNullOrWhiteSpace(dto.Source) || string.IsNullOrWhiteSpace(dto.Title)
            || dto.Entries is null || dto.Entries.Count == 0)
        {
            throw new FormatException(
                "meta.yaml: an otherContent part needs a source, a title and at least one entry.");
        }

        return new OtherContent(
            dto.Source,
            dto.Title,
            dto.Entries.Select(e => ToEntry("otherContent", dto.Source, e)).ToList());
    }

    private static bool ToTestProfile(string chapter, string? value)
    {
        return value switch
        {
            null or "on" => true,
            "off" => false,
            _ => throw new FormatException($"meta.yaml: testProfile of chapter {chapter} must be on or off."),
        };
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

        string? invalid = dto.Sections.FirstOrDefault(s => !SplitSection.IsMatch(s!));
        if (invalid is not null)
        {
            throw new FormatException(
                $"meta.yaml: reference section '{invalid}' of chapter {chapter} must be a section number " +
                "such as 9.12 or D.1, or one after '=' (=9.12) to leave out its subsections.");
        }

        return new ReferenceSplit(dto.File, dto.Sections.Select(s => s!).ToList());
    }

    private sealed class MetaDto
    {
        public string RepositoryUrl { get; set; } = string.Empty;

        public List<ChapterDto> Chapters { get; set; } = new();

        public List<OtherContentDto?>? OtherContent { get; set; }
    }

    private sealed class OtherContentDto
    {
        public string? Source { get; set; }

        public string? Title { get; set; }

        public List<NonGuidelineDto?>? Entries { get; set; }
    }

    private sealed class ChapterDto
    {
        public string Token { get; set; } = string.Empty;

        public string File { get; set; } = string.Empty;

        public string Rules { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public List<NonGuidelineDto> NonGuideline { get; set; } = new();

        public List<ReferenceSplitDto?>? References { get; set; }

        public string? TestProfile { get; set; }
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
