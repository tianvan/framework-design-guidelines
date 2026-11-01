using System.Collections.Immutable;
using System.Composition;
using FrameworkDesignGuidelines.Analyzers;
using FrameworkDesignGuidelines.Analyzers.Layout;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace FrameworkDesignGuidelines.CodeFixes.Layout;

/// <summary>
/// Fixes FDGA003: puts each brace of the block on its own line, aligned with the line of the block's owner
/// (one level further for a switch section), and the statements one per line, one level further in. Block comments
/// after the opening brace stay inside the block, on a line of their own before the first statement. The single-line
/// blocks of a switch section, all reported on its first label, are fixed together.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SingleLineCatchFinallyCodeFix)), Shared]
public sealed class SingleLineCatchFinallyCodeFix : CodeFixProvider
{
    private const string Title = "Place the braces on their own lines";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(FdgDescriptors.FDGA003.Id);

    public override FixAllProvider GetFixAllProvider()
    {
        return WellKnownFixAllProviders.BatchFixer;
    }

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        SyntaxNode? root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return;
        }

        foreach (Diagnostic diagnostic in context.Diagnostics)
        {
            SyntaxToken owner = root.FindToken(diagnostic.Location.SourceSpan.Start);
            ImmutableArray<BlockSyntax> blocks = FindBlocks(owner);
            if (!blocks.IsEmpty)
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        Title,
                        cancellationToken => ExpandAsync(context.Document, blocks, cancellationToken),
                        equivalenceKey: Title),
                    diagnostic);
            }
        }
    }

    /// <summary>
    /// Finds the single-line blocks that <paramref name="owner"/> owns: one at most, except for the first label of a
    /// switch section, which owns every block statement of the section.
    /// </summary>
    private static ImmutableArray<BlockSyntax> FindBlocks(SyntaxToken owner)
    {
        // The owner's parent holds the block, except for a switch label, whose section holds it.
        ImmutableArray<BlockSyntax>.Builder blocks = ImmutableArray.CreateBuilder<BlockSyntax>();
        foreach (SyntaxNode? node in new[] { owner.Parent, owner.Parent?.Parent })
        {
            foreach (BlockSyntax block in node?.ChildNodes().OfType<BlockSyntax>() ?? [])
            {
                if (SingleLineCatchFinallyAnalyzer.TryGetOwner(block, out SyntaxToken blockOwner)
                    && blockOwner == owner
                    && SingleLineCatchFinallyAnalyzer.IsSingleLine(block))
                {
                    blocks.Add(block);
                }
            }
        }

        return blocks.ToImmutable();
    }

    private static async Task<Document> ExpandAsync(
        Document document,
        ImmutableArray<BlockSyntax> blocks,
        CancellationToken cancellationToken)
    {
        SourceText text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        SyntaxNode? root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        // In document order: a block's opening brace is placed after the edit that the block before it makes to the
        // token that follows it, so the brace gets the block's own indentation.
        var edits = new TokenEdits(text);
        foreach (BlockSyntax block in blocks)
        {
            if (SingleLineCatchFinallyAnalyzer.TryGetOwner(block, out SyntaxToken owner))
            {
                Expand(edits, text, block, owner);
            }
        }

        return await edits.ApplyAsync(document, root, cancellationToken).ConfigureAwait(false);
    }

    private static void Expand(TokenEdits edits, SourceText text, BlockSyntax block, SyntaxToken owner)
    {
        string ownerIndentation = LayoutText.GetLineIndentation(text, owner.SpanStart);
        string unit = LayoutText.GetIndentUnit(ownerIndentation);
        string braceIndentation = block.Parent is SwitchSectionSyntax ? ownerIndentation + unit : ownerIndentation;
        string statementIndentation = braceIndentation + unit;

        SyntaxToken open = block.OpenBraceToken;
        SyntaxToken close = block.CloseBraceToken;
        edits.BreakBetween(open.GetPreviousToken(), open, braceIndentation);

        // The block is on one line, so the opening brace shares its line with the token after it. Block comments after
        // the brace get a line of their own inside the block: the formatter would split a comment and a statement that
        // share a line anyway.
        SyntaxToken previous = open;
        foreach (StatementSyntax statement in block.Statements)
        {
            SyntaxToken first = statement.GetFirstToken();
            if (previous == open)
            {
                edits.ForceBreakAroundComments(open, first, statementIndentation, statementIndentation);
            }
            else
            {
                edits.BreakBetween(previous, first, statementIndentation);
            }

            previous = statement.GetLastToken();
        }

        if (previous == open)
        {
            edits.ForceBreakAroundComments(open, close, statementIndentation, braceIndentation);
        }
        else
        {
            edits.BreakBetween(previous, close, braceIndentation);
        }

        SyntaxToken next = close.GetNextToken();
        if (!next.IsKind(SyntaxKind.None))
        {
            edits.BreakBetween(close, next, GetNextTokenIndentation(text, next, ownerIndentation));
        }
    }

    /// <summary>
    /// A token that followed the block on its line, such as <c>else</c> or the next statement, lines up with the
    /// owner; a closing brace lines up with the line of its opening brace. The formatter refines both.
    /// </summary>
    private static string GetNextTokenIndentation(SourceText text, SyntaxToken next, string ownerIndentation)
    {
        if (next.IsKind(SyntaxKind.CloseBraceToken))
        {
            SyntaxToken parentOpen = next.Parent?.ChildTokens().FirstOrDefault(token => token.IsKind(SyntaxKind.OpenBraceToken)) ?? default;
            if (parentOpen.IsKind(SyntaxKind.OpenBraceToken))
            {
                return LayoutText.GetLineIndentation(text, parentOpen.SpanStart);
            }
        }

        return ownerIndentation;
    }
}
