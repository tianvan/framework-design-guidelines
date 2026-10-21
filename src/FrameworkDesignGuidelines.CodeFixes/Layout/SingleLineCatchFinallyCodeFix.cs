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
/// (one level further for a switch section), and the statements one per line, one level further in.
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
            if (FindBlock(owner) is BlockSyntax block)
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        Title,
                        cancellationToken => ExpandAsync(context.Document, block, cancellationToken),
                        equivalenceKey: Title),
                    diagnostic);
            }
        }
    }

    private static BlockSyntax? FindBlock(SyntaxToken owner)
    {
        // The owner's parent holds the block, except for a switch label, whose section holds it.
        foreach (SyntaxNode? node in new[] { owner.Parent, owner.Parent?.Parent })
        {
            foreach (BlockSyntax block in node?.ChildNodes().OfType<BlockSyntax>() ?? [])
            {
                if (SingleLineCatchFinallyAnalyzer.TryGetOwner(block, out SyntaxToken blockOwner)
                    && blockOwner == owner
                    && SingleLineCatchFinallyAnalyzer.IsSingleLine(block))
                {
                    return block;
                }
            }
        }

        return null;
    }

    private static async Task<Document> ExpandAsync(Document document, BlockSyntax block, CancellationToken cancellationToken)
    {
        SourceText text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        SyntaxNode? root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || !SingleLineCatchFinallyAnalyzer.TryGetOwner(block, out SyntaxToken owner))
        {
            return document;
        }

        string ownerIndentation = LayoutText.GetLineIndentation(text, owner.SpanStart);
        string braceIndentation = block.Parent is SwitchSectionSyntax
            ? ownerIndentation + LayoutText.IndentUnit
            : ownerIndentation;
        string statementIndentation = braceIndentation + LayoutText.IndentUnit;

        var edits = new TokenEdits(text);
        SyntaxToken open = block.OpenBraceToken;
        SyntaxToken close = block.CloseBraceToken;
        edits.BreakBetween(open.GetPreviousToken(), open, braceIndentation);

        SyntaxToken previous = open;
        foreach (StatementSyntax statement in block.Statements)
        {
            edits.BreakBetween(previous, statement.GetFirstToken(), statementIndentation);
            previous = statement.GetLastToken();
        }

        edits.BreakBetween(previous, close, braceIndentation);

        SyntaxToken next = close.GetNextToken();
        if (!next.IsKind(SyntaxKind.None))
        {
            edits.BreakBetween(close, next, GetNextTokenIndentation(text, next, ownerIndentation));
        }

        return document.WithSyntaxRoot(edits.Apply(root));
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
