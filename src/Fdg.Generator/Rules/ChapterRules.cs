namespace Fdg.Generator.Rules;

/// <summary>The rules loaded from one chapter's rule file.</summary>
public sealed record ChapterRules(ChapterMeta Chapter, IReadOnlyList<Rule> Rules);
