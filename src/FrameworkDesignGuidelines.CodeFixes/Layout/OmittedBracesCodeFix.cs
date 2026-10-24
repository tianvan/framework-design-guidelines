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
/// Fixes FDGA004: encloses the embedded statement in braces, each on its own line aligned with the line of the
/// statement's keyword, and puts the statement on its own line one level further in. Comments stay where they were;
/// the closing brace of a <c>do</c> body stays on the line of its <c>while</c>.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(OmittedBracesCodeFix)), Shared]
public sealed class OmittedBracesCodeFix : CodeFixProvider
{
    private const string Title = "Enclose the statement in braces";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(FdgDescriptors.FDGA004.Id);

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
            SyntaxNode? owner = root.FindToken(diagnostic.Location.SourceSpan.Start).Parent;
            if (owner is not null && OmittedBracesAnalyzer.IsReported(owner, out _, out _))
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        Title,
                        cancellationToken => EncloseAsync(context.Document, owner, cancellationToken),
                        equivalenceKey: Title),
                    diagnostic);
            }
        }
    }

    private static async Task<Document> EncloseAsync(Document document, SyntaxNode owner, CancellationToken cancellationToken)
    {
        SourceText text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        if (!OmittedBracesAnalyzer.IsReported(owner, out SyntaxToken keyword, out StatementSyntax? statement) || statement is null)
        {
            return document;
        }

        string endOfLine = TokenEdits.DetectEndOfLine(text);
        string indentation = LayoutText.GetLineIndentation(text, keyword.SpanStart);
        string innerIndentation = indentation + LayoutText.GetIndentUnit(indentation);
        SyntaxToken first = statement.GetFirstToken();
        SyntaxToken previous = first.GetPreviousToken();
        SyntaxToken last = statement.GetLastToken();
        SyntaxToken next = last.GetNextToken();

        var changes = new List<TextChange>();
        if (LayoutText.GetLineNumber(text, previous.Span.End) == LayoutText.GetLineNumber(text, first.SpanStart))
        {
            changes.Add(new TextChange(
                WhitespaceAfter(text, previous.Span.End),
                endOfLine + indentation + "{" + endOfLine + innerIndentation));
        }
        else
        {
            // At the end of the keyword's line, after any comment there.
            int lineEnd = text.Lines.GetLineFromPosition(previous.Span.End).End;
            changes.Add(new TextChange(new TextSpan(lineEnd, 0), endOfLine + indentation + "{"));
        }

        if (next.IsKind(SyntaxKind.None)
            || LayoutText.GetLineNumber(text, last.Span.End) != LayoutText.GetLineNumber(text, next.SpanStart))
        {
            int lineEnd = text.Lines.GetLineFromPosition(last.Span.End).End;
            changes.Add(new TextChange(new TextSpan(lineEnd, 0), endOfLine + indentation + "}"));
        }
        else
        {
            bool isDoWhile = owner is DoStatementSyntax && next.IsKind(SyntaxKind.WhileKeyword);
            string afterBrace = isDoWhile ? " " : endOfLine + indentation;
            changes.Add(new TextChange(
                WhitespaceAfter(text, last.Span.End),
                endOfLine + indentation + "}" + afterBrace));
        }

        return document.WithText(text.WithChanges(changes));
    }

    /// <summary>The run of spaces and tabs that starts at <paramref name="position"/>.</summary>
    private static TextSpan WhitespaceAfter(SourceText text, int position)
    {
        int end = position;
        while (end < text.Length && text[end] is ' ' or '\t')
        {
            end++;
        }

        return TextSpan.FromBounds(position, end);
    }
}
