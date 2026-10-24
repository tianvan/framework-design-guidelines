using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace FrameworkDesignGuidelines.Analyzers.Layout;

/// <summary>
/// FDGA023: a parameter or argument list that is exceedingly long must be chopped: a line break after the
/// opening parenthesis, one item per line indented one level from the line of the opening parenthesis, and the
/// closing parenthesis right after the last item. A list is exceedingly long when its line is longer than
/// <c>max_line_length</c>, or when the author wrapped it over several lines.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LongListWrappingAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(FdgDescriptors.FDGA023);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSyntaxNodeAction(
            AnalyzeList,
            SyntaxKind.ParameterList,
            SyntaxKind.BracketedParameterList,
            SyntaxKind.ArgumentList);
    }

    /// <summary>
    /// Recognizes the parameter lists of methods, constructors, delegates, local functions, operators and
    /// indexers, and the argument lists of invocations, object creations and constructor initializers, when
    /// they have at least two items.
    /// </summary>
    internal static bool TryGetList(SyntaxNode node, out ListShape shape)
    {
        shape = node switch
        {
            ParameterListSyntax list when list.Parent is MethodDeclarationSyntax
                or ConstructorDeclarationSyntax
                or DelegateDeclarationSyntax
                or LocalFunctionStatementSyntax
                or OperatorDeclarationSyntax
                or ConversionOperatorDeclarationSyntax
                => new ListShape(list.OpenParenToken, list.Parameters, list.CloseParenToken),
            BracketedParameterListSyntax list when list.Parent is IndexerDeclarationSyntax
                => new ListShape(list.OpenBracketToken, list.Parameters, list.CloseBracketToken),
            ArgumentListSyntax list when list.Parent is InvocationExpressionSyntax
                or BaseObjectCreationExpressionSyntax
                or ConstructorInitializerSyntax
                => new ListShape(list.OpenParenToken, list.Arguments, list.CloseParenToken),
            _ => default,
        };

        return shape.Items is { Count: >= 2 } && !shape.Open.IsMissing && !shape.Close.IsMissing;
    }

    /// <summary>
    /// True when the list is already chopped: the first item starts on a line after the opening parenthesis,
    /// every item starts its own line at one indentation level past the opening parenthesis's line, and the
    /// closing parenthesis is on the line where the last item ends, or on the next line when a line comment
    /// ends the last item's line.
    /// </summary>
    internal static bool IsChopped(SourceText text, ListShape shape)
    {
        int expectedWidth = LayoutText.GetWidth(LayoutText.GetLineIndentation(text, shape.Open.SpanStart))
            + LayoutText.IndentUnit.Length;
        SyntaxToken previous = shape.Open;
        foreach (SyntaxNode item in shape.Items)
        {
            int start = item.SpanStart;
            if (LayoutText.GetLineNumber(text, previous.Span.End) == LayoutText.GetLineNumber(text, start))
            {
                return false;
            }

            TextLine line = text.Lines.GetLineFromPosition(start);
            string indentation = LayoutText.GetLineIndentation(text, start);
            if (line.Start + indentation.Length != start || LayoutText.GetWidth(indentation) != expectedWidth)
            {
                return false;
            }

            previous = item.GetLastToken().GetNextToken();
        }

        SyntaxToken last = shape.Items[shape.Items.Count - 1].GetLastToken();
        return LayoutText.GetLineNumber(text, last.Span.End) == LayoutText.GetLineNumber(text, shape.Close.SpanStart)
            || last.TrailingTrivia.Any(SyntaxKind.SingleLineCommentTrivia);
    }

    /// <summary>True when the list is written over more than one line.</summary>
    private static bool IsWrapped(SourceText text, ListShape shape)
    {
        return LayoutText.GetLineNumber(text, shape.Open.SpanStart) != LayoutText.GetLineNumber(text, shape.Close.SpanStart);
    }

    private static void AnalyzeList(SyntaxNodeAnalysisContext context)
    {
        if (!TryGetList(context.Node, out ListShape shape))
        {
            return;
        }

        SyntaxTree tree = context.Node.SyntaxTree;
        SourceText text = tree.GetText(context.CancellationToken);
        TextLine line = text.Lines.GetLineFromPosition(shape.Open.SpanStart);
        int maxLength = LineLength.Get(context.Options.AnalyzerConfigOptionsProvider.GetOptions(tree));
        if ((line.Span.Length <= maxLength && !IsWrapped(text, shape))
            || IsChopped(text, shape)
            || HasEarlierListOnLine(context.Node.SyntaxTree.GetRoot(context.CancellationToken), shape, line))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDGA023, shape.Open.GetLocation()));
    }

    /// <summary>
    /// Only the leftmost list opening on a line is reported: the one enclosing the others, or the first link of a
    /// fluent chain. The rest are left for the next run, which then indents them from their new line.
    /// </summary>
    private static bool HasEarlierListOnLine(SyntaxNode root, ListShape shape, TextLine line)
    {
        for (SyntaxToken token = root.FindToken(line.Start);
            token.SpanStart < shape.Open.SpanStart && !token.IsKind(SyntaxKind.None);
            token = token.GetNextToken())
        {
            if (token.SpanStart >= line.Start
                && (token.IsKind(SyntaxKind.OpenParenToken) || token.IsKind(SyntaxKind.OpenBracketToken))
                && token.Parent is SyntaxNode list
                && TryGetList(list, out ListShape earlier)
                && earlier.Open == token)
            {
                return true;
            }
        }

        return false;
    }

    internal readonly struct ListShape
    {
        public ListShape(SyntaxToken open, IReadOnlyList<SyntaxNode> items, SyntaxToken close)
        {
            Open = open;
            Items = items;
            Close = close;
        }

        public SyntaxToken Open { get; }

        public IReadOnlyList<SyntaxNode> Items { get; }

        public SyntaxToken Close { get; }
    }
}
