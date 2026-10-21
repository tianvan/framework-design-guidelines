using System.Collections.Immutable;
using System.Composition;
using FrameworkDesignGuidelines.Analyzers;
using FrameworkDesignGuidelines.Analyzers.Layout;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Text;

namespace FrameworkDesignGuidelines.CodeFixes.Layout;

/// <summary>
/// Fixes FDGA023: chops the list before its first item, one item per line indented one level past the line of
/// the opening parenthesis, with the closing parenthesis after the last item. Item text is kept as written;
/// a long list inside an item is left for the next run.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(LongListWrappingCodeFix)), Shared]
public sealed class LongListWrappingCodeFix : CodeFixProvider
{
    private const string Title = "Place each item on its own line";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(FdgDescriptors.FDGA023.Id);

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
            SyntaxNode? list = root.FindToken(diagnostic.Location.SourceSpan.Start).Parent;
            if (list is not null && LongListWrappingAnalyzer.TryGetList(list, out _))
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        Title,
                        cancellationToken => ChopAsync(context.Document, list, cancellationToken),
                        equivalenceKey: Title),
                    diagnostic);
            }
        }
    }

    private static async Task<Document> ChopAsync(Document document, SyntaxNode list, CancellationToken cancellationToken)
    {
        SourceText text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        SyntaxNode? root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || !LongListWrappingAnalyzer.TryGetList(list, out LongListWrappingAnalyzer.ListShape shape))
        {
            return document;
        }

        string lineIndentation = LayoutText.GetLineIndentation(text, shape.Open.SpanStart);
        string itemIndentation = lineIndentation + LayoutText.GetIndentUnit(lineIndentation);
        bool useTabs = itemIndentation.IndexOf('\t') >= 0;
        int itemWidth = LayoutText.GetWidth(itemIndentation);
        var edits = new TokenEdits(text);
        SyntaxToken previous = shape.Open;
        foreach (SyntaxNode item in shape.Items)
        {
            SyntaxToken first = item.GetFirstToken();
            SyntaxToken last = item.GetLastToken();
            edits.ForceBreakBetween(previous, first, itemIndentation);

            // The lines of a multi-line item (an initializer, a lambda body) move with its first line.
            int originalWidth = LayoutText.GetWidth(LayoutText.GetLineIndentation(text, first.SpanStart));
            edits.ShiftContinuationLines(item, itemWidth - originalWidth, useTabs);

            previous = last.GetNextToken();
            edits.Join(last, previous, itemIndentation);
        }

        return await edits.ApplyAsync(document, root, cancellationToken).ConfigureAwait(false);
    }
}
