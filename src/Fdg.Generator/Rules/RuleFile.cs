namespace Fdg.Generator.Rules;

/// <summary>One chapter's rules together with the chapter facts recorded in <c>meta.yaml</c>.</summary>
public sealed record RuleFile(string Chapter, string Title, string SourceFile, IReadOnlyList<Rule> Rules);
