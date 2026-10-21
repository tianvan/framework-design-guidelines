namespace Fdg.Generator.Rules;

/// <summary>One chapter's rules together with the chapter facts recorded in <c>meta.yaml</c>.</summary>
public sealed record RuleFile(string Chapter, string Title, string SourceFile, IReadOnlyList<Rule> Rules)
{
    /// <summary>The file name of the chapter's rule file, such as <c>ch03-naming.yaml</c>.</summary>
    public string RulesFile { get; init; } = string.Empty;

    public IReadOnlyList<NonGuidelineEntry> NonGuideline { get; init; } = Array.Empty<NonGuidelineEntry>();
}
