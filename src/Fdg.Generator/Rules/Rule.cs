namespace Fdg.Generator.Rules;

public sealed record Rule(
    string Id,
    Strength Strength,
    string Text,
    string? Context,
    string Section,
    string Anchor,
    IReadOnlyList<Block> Body,
    RuleMapping Mapping);
