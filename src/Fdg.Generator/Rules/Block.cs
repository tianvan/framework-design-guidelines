namespace Fdg.Generator.Rules;

public enum BlockKind
{
    Paragraph,
    Code,
    Annotation,
    List,
    Table,
}

public sealed record Block(BlockKind Kind, string Text, string? Author = null);
